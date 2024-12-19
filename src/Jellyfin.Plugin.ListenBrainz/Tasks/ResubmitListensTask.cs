using Jellyfin.Plugin.ListenBrainz.Api.Models;
using Jellyfin.Plugin.ListenBrainz.Api.Resources;
using Jellyfin.Plugin.ListenBrainz.Common.Extensions;
using Jellyfin.Plugin.ListenBrainz.Configuration;
using Jellyfin.Plugin.ListenBrainz.Dtos;
using Jellyfin.Plugin.ListenBrainz.Exceptions;
using Jellyfin.Plugin.ListenBrainz.Extensions;
using Jellyfin.Plugin.ListenBrainz.Interfaces;
using Jellyfin.Plugin.ListenBrainz.Managers;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using ClientUtils = Jellyfin.Plugin.ListenBrainz.Clients.Utils;

namespace Jellyfin.Plugin.ListenBrainz.Tasks;

/// <summary>
/// Jellyfin scheduled task for resubmitting listens.
/// </summary>
public class ResubmitListensTask : IScheduledTask
{
    private readonly ILogger _logger;
    private readonly ListensCacheManager _listensCache;
    private readonly IListenBrainzClient _listenBrainzClient;
    private readonly IMusicBrainzClient _musicBrainzClient;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResubmitListensTask"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="clientFactory">HTTP client factory.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    public ResubmitListensTask(
        ILoggerFactory loggerFactory,
        IHttpClientFactory clientFactory,
        IUserManager userManager,
        ILibraryManager libraryManager)
    {
        _logger = loggerFactory.CreateLogger($"{Plugin.LoggerCategory}.ResubmitListensTask");
        _listensCache = ListensCacheManager.Instance;
        _listenBrainzClient = ClientUtils.GetListenBrainzClient(_logger, clientFactory, libraryManager);
        _musicBrainzClient = ClientUtils.GetMusicBrainzClient(_logger, clientFactory);
        _userManager = userManager;
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public string Name => "Resubmit listens";

    /// <inheritdoc />
    public string Key => "ResubmitListens";

    /// <inheritdoc />
    public string Description => "Attempt to resubmit listens in cache to ListenBrainz.";

    /// <inheritdoc />
    public string Category => "ListenBrainz";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.GetConfiguration();
        await _listensCache.RestoreAsync();

        try
        {
            foreach (var userConfig in config.UserConfigs)
            {
                if (_listensCache.GetListens(userConfig.JellyfinUserId).Any())
                {
                    _logger.LogInformation(
                        "Found listens in cache for user {UserId}, will try resubmitting",
                        userConfig.JellyfinUserId);
                    cancellationToken.ThrowIfCancellationRequested();
                    await SubmitListensForUser(config, userConfig.JellyfinUserId, cancellationToken);
                }
                else
                {
                    _logger.LogInformation(
                        "User {UserId} does not have any cached listens, skipping",
                        userConfig.JellyfinUserId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Listen resubmitting has been cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Listen resubmitting failed: {Reason}", ex.Message);
            _logger.LogDebug(ex, "Listen resubmitting failed");
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = GetInterval()
            }
        };
    }

    private static long GetInterval()
    {
        var random = new Random();
        var randomMinute = random.Next(50);
        return TimeSpan.TicksPerDay + (randomMinute * TimeSpan.TicksPerMinute);
    }

    private async Task SubmitListensForUser(
        PluginConfiguration pluginConfig,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            throw new PluginException("Invalid Jellyfin user ID");
        }

        var userConfig = user.GetListenBrainzConfig();
        if (userConfig is null)
        {
            throw new PluginException($"No configuration for user {user.Username}");
        }

        var listenChunks = _listensCache.GetListens(userId).Chunk(Limits.MaxListensPerRequest);
        foreach (var listenChunk in listenChunks)
        {
            var listensToSend = listenChunk.Select(listen =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var processedListen = pluginConfig.IsMusicBrainzEnabled
                        ? UpdateMetadataIfNecessary(listen)
                        : listen;

                    try
                    {
                        var convertedListen = ToListen(processedListen);
                        _listensCache.RemoveListen(userId, processedListen);
                        return convertedListen;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogInformation("Failed to prepare cached listen for submission: {Reason}", ex.Message);
                        _logger.LogDebug(ex, "Recreating listen from cache failed");
                        return null;
                    }
                })
                .WhereNotNull();

            try
            {
                await _listenBrainzClient.SendListensAsync(userConfig, listensToSend, cancellationToken);
                await _listensCache.SaveAsync();
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Failed to resubmit listens for user {User}: {Reason}", userId, ex.Message);
                _logger.LogDebug(ex, "Listen resubmit failed");
                break;
            }
        }
    }

    private StoredListen UpdateMetadataIfNecessary(StoredListen listen)
    {
        if (listen.Metadata is not null && !string.IsNullOrEmpty(listen.Metadata.RecordingMbid))
        {
            return listen;
        }

        try
        {
            if (_libraryManager.GetItemById(listen.Id) is not Audio item)
            {
                return listen;
            }

            listen.Metadata = _musicBrainzClient.GetAudioItemMetadata(item);
        }
        catch (Exception e)
        {
            _logger.LogInformation("No additional metadata available: {Reason}", e.Message);
            _logger.LogDebug(e, "No additional metadata available");
        }

        return listen;
    }

    /// <summary>
    /// Convert a <see cref="StoredListen"/> to <see cref="Listen"/>s.
    /// </summary>
    /// <param name="storedListen">Stored listen to convert.</param>
    /// <returns>Converted listens.</returns>
    private Listen? ToListen(StoredListen storedListen)
    {
        if (_libraryManager is null)
        {
            throw new InvalidOperationException("Library manager is not available");
        }

        var baseItem = _libraryManager.GetItemById(storedListen.Id);
        var audio = (Audio?)baseItem;
        return audio?.AsListen(storedListen.ListenedAt, storedListen.Metadata);
    }
}
