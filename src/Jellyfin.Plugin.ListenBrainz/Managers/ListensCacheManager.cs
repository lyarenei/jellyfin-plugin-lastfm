using System.Text.Json;
using Jellyfin.Plugin.ListenBrainz.Dtos;
using Jellyfin.Plugin.ListenBrainz.Exceptions;
using Jellyfin.Plugin.ListenBrainz.Extensions;
using Jellyfin.Plugin.ListenBrainz.Interfaces;
using MediaBrowser.Controller.Entities.Audio;

namespace Jellyfin.Plugin.ListenBrainz.Managers;

/// <summary>
/// Cache manager.
/// </summary>
public sealed class ListensCacheManager : ICacheManager, IListensCache, IDisposable
{
    /// <summary>
    /// Cache file name.
    /// </summary>
    private const string CacheFileName = "cache.json";

    /// <summary>
    /// JSON serializer options.
    /// </summary>
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _cachePath;
    private readonly SemaphoreSlim _lock;
    private static ListensCacheManager? _instance;
    private Dictionary<Guid, List<StoredListen>> _listensCache;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListensCacheManager"/> class.
    /// </summary>
    /// <param name="cacheFilePath">Path to the cache file.</param>
    /// <param name="restore">Restore state from cache file.</param>
    public ListensCacheManager(string cacheFilePath, bool restore = true)
    {
        _cachePath = cacheFilePath;
        _listensCache = new Dictionary<Guid, List<StoredListen>>();
        _lock = new SemaphoreSlim(1, 1);
        _isDisposed = false;

        if (restore && File.Exists(_cachePath))
        {
            Restore();
        }
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="ListensCacheManager"/> class.
    /// </summary>
    ~ListensCacheManager() => Dispose(false);

    /// <summary>
    /// Gets instance of the cache manager.
    /// </summary>
    public static ListensCacheManager Instance
    {
        get
        {
            if (_instance is not null)
            {
                return _instance;
            }

            var path = Path.Join(Plugin.GetDataPath(), CacheFileName);
            _instance = new ListensCacheManager(path);
            if (!File.Exists(path))
            {
                _instance.Save();
            }

            return _instance;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Dispose managed and unmanaged (own) resources.
    /// </summary>
    /// <param name="disposing">Dispose managed resources.</param>
    private void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _lock.Dispose();
        }

        _isDisposed = true;
    }

    /// <inheritdoc />
    public void Save()
    {
        _lock.Wait();
        try
        {
            using var stream = File.Create(_cachePath);
            JsonSerializer.Serialize(stream, _listensCache, _serializerOptions);
        }
        catch (Exception ex)
        {
            throw new PluginException("Saving cache failed", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var stream = File.Create(_cachePath);
            await JsonSerializer.SerializeAsync(stream, _listensCache, _serializerOptions);
        }
        catch (Exception ex)
        {
            throw new PluginException("Saving cache failed", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    /// <exception cref="PluginException">Restore failed.</exception>
    public void Restore()
    {
        _lock.Wait();
        Dictionary<Guid, List<StoredListen>>? data;
        try
        {
            using var stream = File.OpenRead(_cachePath);
            data = JsonSerializer.Deserialize<Dictionary<Guid, List<StoredListen>>>(stream, _serializerOptions);
        }
        catch (Exception ex)
        {
            throw new PluginException("Cache restore failed", ex);
        }
        finally
        {
            _lock.Release();
        }

        _listensCache = data ?? throw new PluginException("Failed to deserialize data from cache file");
    }

    /// <inheritdoc />
    /// <exception cref="PluginException">Restore failed.</exception>
    public async Task RestoreAsync()
    {
        await _lock.WaitAsync();
        Dictionary<Guid, List<StoredListen>>? data;
        try
        {
            await using var stream = File.OpenRead(_cachePath);
            data = await JsonSerializer.DeserializeAsync<Dictionary<Guid, List<StoredListen>>>(stream, _serializerOptions);
        }
        catch (Exception ex)
        {
            throw new PluginException("Cache restore failed", ex);
        }
        finally
        {
            _lock.Release();
        }

        _listensCache = data ?? throw new PluginException("Failed to deserialize data from cache file");
    }

    /// <inheritdoc />
    public void AddListen(Guid userId, Audio item, AudioItemMetadata? metadata, long listenedAt)
    {
        _lock.Wait();
        try
        {
            _listensCache[userId].Add(item.AsStoredListen(listenedAt, metadata));
        }
        catch (KeyNotFoundException)
        {
            _listensCache[userId] = new List<StoredListen> { item.AsStoredListen(listenedAt, metadata) };
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task AddListenAsync(Guid userId, Audio item, AudioItemMetadata? metadata, long listenedAt)
    {
        await _lock.WaitAsync();
        try
        {
            _listensCache[userId].Add(item.AsStoredListen(listenedAt, metadata));
        }
        catch (KeyNotFoundException)
        {
            _listensCache[userId] = new List<StoredListen> { item.AsStoredListen(listenedAt, metadata) };
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public IEnumerable<StoredListen> GetListens(Guid userId)
    {
        if (_listensCache.TryGetValue(userId, out var listens))
        {
            return listens;
        }

        return Array.Empty<StoredListen>();
    }

    /// <inheritdoc />
    public void RemoveListen(Guid userId, StoredListen listen)
    {
        _lock.Wait();
        try
        {
            if (_listensCache.TryGetValue(userId, out var userListens))
            {
                userListens.Remove(listen);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void RemoveListens(Guid userId, IEnumerable<StoredListen> listens)
    {
        _lock.Wait();
        var storedListens = listens.Select(sl => sl.ListenedAt).ToList();
        try
        {
            if (_listensCache.TryGetValue(userId, out var userListens))
            {
                userListens.RemoveAll(sl => storedListens.Contains(sl.ListenedAt));
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RemoveListensAsync(Guid userId, IEnumerable<StoredListen> listens)
    {
        await _lock.WaitAsync();
        var storedListens = listens.Select(sl => sl.ListenedAt).ToList();
        try
        {
            if (_listensCache.TryGetValue(userId, out var userListens))
            {
                userListens.RemoveAll(sl => storedListens.Contains(sl.ListenedAt));
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
