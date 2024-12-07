using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.ListenBrainz.Api.Models;
using Jellyfin.Plugin.ListenBrainz.Common;
using Jellyfin.Plugin.ListenBrainz.Dtos;
using Jellyfin.Plugin.ListenBrainz.Exceptions;
using Jellyfin.Plugin.ListenBrainz.Extensions;
using Jellyfin.Plugin.ListenBrainz.Interfaces;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ListenBrainz.Managers;

/// <summary>
/// Listens backup manager.
/// </summary>
public class BackupManager : IBackupManager
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock;
    private bool _isDisposed;

    /// <summary>
    /// JSON serializer options.
    /// </summary>
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="BackupManager"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public BackupManager(ILogger logger)
    {
        _logger = logger;
        _lock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="BackupManager"/> class.
    /// </summary>
    ~BackupManager() => Dispose(false);

    /// <inheritdoc />
    public void Backup(string userName, Audio item, AudioItemMetadata? metadata, long timestamp)
    {
        var config = Plugin.GetConfiguration();
        var dirPath = Path.Combine(config.BackupPath, userName);
        var filePath = Path.Combine(dirPath, $"{DateUtils.TodayIso}.json");
        List<Listen>? userListens = null;

        _logger.LogDebug("Backing up listen of {SongName} to {FileName}", item.Name, filePath);

        _lock.Wait();
        try
        {
            var dirInfo = new DirectoryInfo(dirPath);
            if (dirInfo.Exists)
            {
                using var stream = File.OpenRead(filePath);
                userListens = JsonSerializer.Deserialize<List<Listen>>(stream, _serializerOptions);
            }
            else
            {
                _logger.LogDebug("Directory does not exist, it will be created");
                dirInfo.Create();
            }
        }
        catch (FileNotFoundException)
        {
            _logger.LogDebug("File does not exist, it will be created");
        }
        catch (Exception ex)
        {
            throw new PluginException("Failed to read backup file", ex);
        }

        userListens ??= new List<Listen>();
        userListens.Add(item.AsListen(timestamp, metadata));

        try
        {
            using var stream = File.Create(filePath);
            JsonSerializer.Serialize(stream, userListens, _serializerOptions);
        }
        catch (Exception ex)
        {
            throw new PluginException("Listen backup failed", ex);
        }
        finally
        {
            _lock.Release();
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
}
