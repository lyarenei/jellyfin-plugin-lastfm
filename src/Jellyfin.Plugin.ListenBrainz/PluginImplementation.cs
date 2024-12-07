using Jellyfin.Data.Entities;
using Jellyfin.Plugin.ListenBrainz.Api.Resources;
using Jellyfin.Plugin.ListenBrainz.Common;
using Jellyfin.Plugin.ListenBrainz.Configuration;
using Jellyfin.Plugin.ListenBrainz.Dtos;
using Jellyfin.Plugin.ListenBrainz.Exceptions;
using Jellyfin.Plugin.ListenBrainz.Extensions;
using Jellyfin.Plugin.ListenBrainz.Interfaces;
using Jellyfin.Plugin.ListenBrainz.Managers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ListenBrainz;

/// <summary>
/// ListenBrainz plugin implementation.
/// </summary>
public class PluginImplementation : IDisposable
{
    private readonly ILogger _logger;
    private readonly IListenBrainzClient _listenBrainzClient;
    private readonly IMusicBrainzClient _musicBrainzClient;
    private readonly IUserDataManager _userDataManager;
    private readonly ListensCacheManager _listensCache;
    private readonly IUserManager _userManager;
    private readonly object _userDataSaveLock = new();
    private readonly PlaybackTrackingManager _playbackTracker;
    private readonly ILibraryManager _libraryManager;
    private readonly IBackupManager _backupManager;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginImplementation"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="listenBrainzClient">ListenBrainz client.</param>
    /// <param name="musicBrainzClient">Client for providing additional metadata.</param>
    /// <param name="userDataManager">User data manager.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="backupManager">Backup manager.</param>
    public PluginImplementation(
        ILogger logger,
        IListenBrainzClient listenBrainzClient,
        IMusicBrainzClient musicBrainzClient,
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IBackupManager backupManager)
    {
        _logger = logger;
        _listenBrainzClient = listenBrainzClient;
        _musicBrainzClient = musicBrainzClient;
        _userDataManager = userDataManager;
        _listensCache = ListensCacheManager.Instance;
        _userManager = userManager;
        _playbackTracker = PlaybackTrackingManager.Instance;
        _libraryManager = libraryManager;
        _backupManager = backupManager;
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="PluginImplementation"/> class.
    /// </summary>
    ~PluginImplementation() => Dispose(false);

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
            _backupManager.Dispose();
        }

        _isDisposed = true;
    }

    /// <summary>
    /// Sends 'playing now' listen to ListenBrainz.
    /// </summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="args">Event args.</param>
    public void OnPlaybackStart(object? sender, PlaybackProgressEventArgs args)
    {
        using var logScope = BeginLogScope();
        _logger.LogDebug("Picking up playback start event for item {Item}", args.Item.Name);
        EventData data;
        try
        {
            data = GetEventData(args);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Dropping event");
            return;
        }

        _logger.LogInformation(
            "Processing playback start event for item {Item} associated with user {Username}",
            data.Item.Name,
            data.JellyfinUser.Username);

        UserConfig userConfig;
        try
        {
            AssertInAllowedLibrary(data.Item);
            userConfig = data.JellyfinUser.GetListenBrainzConfig();
            AssertSubmissionEnabled(userConfig);
            AssertBasicMetadataRequirements(data.Item);
        }
        catch (Exception e)
        {
            _logger.LogInformation("Dropping event: {Reason}", e.Message);
            _logger.LogDebug(e, "Dropping event");
            return;
        }

        _logger.LogDebug("All checks passed, preparing for sending listen");

        AudioItemMetadata? metadata = null;
        try
        {
            AssertMusicBrainzIsEnabled();
            _logger.LogInformation("Getting additional metadata...");
            metadata = _musicBrainzClient.GetAudioItemMetadata(data.Item);
            _logger.LogInformation("Additional metadata successfully received");
        }
        catch (Exception e)
        {
            _logger.LogInformation("Additional metadata are not available: {Reason}", e.Message);
            _logger.LogDebug(e, "Additional metadata are not available");
        }

        try
        {
            _logger.LogInformation("Sending 'playing now' listen...");
            _listenBrainzClient.SendNowPlaying(userConfig, data.Item, metadata);
            _logger.LogInformation("Successfully sent 'playing now' listen");
        }
        catch (Exception e)
        {
            _logger.LogInformation("Failed to send 'playing now' listen: {Reason}", e.Message);
            _logger.LogDebug(e, "Failed to send 'playing now' listen");
        }

        if (Plugin.GetConfiguration().IsAlternativeModeEnabled)
        {
            _logger.LogDebug("Alternative mode is enabled, adding item to playback tracker");
            _playbackTracker.AddItem(data.JellyfinUser.Id.ToString(), data.Item);
        }

        _logger.LogDebug("Event has been successfully processed");
    }

    /// <summary>
    /// Sends listen of track to ListenBrainz if conditions have been met.
    /// </summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="args">Event args.</param>
    public void OnPlaybackStop(object? sender, PlaybackStopEventArgs args)
    {
        using var logScope = BeginLogScope();
        _logger.LogDebug("Picking up playback stop event for item {Item}", args.Item.Name);
        var config = Plugin.GetConfiguration();
        if (config.IsAlternativeModeEnabled)
        {
            _logger.LogDebug("Dropping event - alternative mode is enabled");
            return;
        }

        EventData data;
        try
        {
            data = GetEventData(args);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Dropping event");
            return;
        }

        _logger.LogInformation(
            "Processing playback stop event for item {Item} associated with user {Username}",
            data.Item.Name,
            data.JellyfinUser.Username);

        UserConfig userConfig;
        try
        {
            AssertInAllowedLibrary(data.Item);
            userConfig = data.JellyfinUser.GetListenBrainzConfig();
            AssertSubmissionEnabled(userConfig);
            AssertBasicMetadataRequirements(data.Item);
            AssertListenBrainzRequirements(args, data);
        }
        catch (Exception e)
        {
            _logger.LogInformation("Dropping event: {Reason}", e.Message);
            _logger.LogDebug(e, "Dropping event");
            return;
        }

        _logger.LogDebug("All checks passed, preparing for sending listen");
        var now = DateUtils.CurrentTimestamp;

        AudioItemMetadata? metadata = null;
        try
        {
            AssertMusicBrainzIsEnabled();
            _logger.LogInformation("Getting additional metadata...");
            metadata = _musicBrainzClient.GetAudioItemMetadata(data.Item);
            _logger.LogInformation("Additional metadata successfully received");
        }
        catch (Exception e)
        {
            _logger.LogInformation("Additional metadata are not available: {Reason}", e.Message);
            _logger.LogDebug(e, "Additional metadata are not available");
        }

        if (config.IsBackupEnabled && userConfig.IsBackupEnabled)
        {
            try
            {
                _logger.LogInformation("Adding listen to backups...");
                _backupManager.Backup(userConfig.UserName, data.Item, metadata, now);
                _logger.LogInformation("Listen successfully backed up");
            }
            catch (Exception e)
            {
                _logger.LogInformation("Listen backup failed: {Reason}", e.Message);
                _logger.LogDebug(e, "Listen backup failed");
            }
        }

        try
        {
            _logger.LogInformation("Sending listen...");
            _listenBrainzClient.SendListen(userConfig, data.Item, metadata, now);
            _logger.LogInformation("Listen successfully sent");
        }
        catch (Exception e)
        {
            _logger.LogInformation("Failed to send listen: {Reason}", e.Message);
            _logger.LogDebug(e, "Failed to send listen");

            _listensCache.AddListen(data.JellyfinUser.Id, data.Item, metadata, now);
            _listensCache.Save();
            _logger.LogInformation("Listen has been added to the cache");
            return;
        }

        if (userConfig.IsFavoritesSyncEnabled)
        {
            HandleFavoriteSync(data, metadata, userConfig, now);
        }
    }

    /// <summary>
    /// Sends listen of track to ListenBrainz if conditions have been met.
    /// </summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="args">Event args.</param>
    public void OnUserDataSave(object? sender, UserDataSaveEventArgs args)
    {
        using var logScope = BeginLogScope();
        _logger.LogDebug("Picking up user data save event for item {Item}", args.Item.Name);

        EventData data;
        try
        {
            data = GetEventData(args);
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Dropping event");
            return;
        }

        switch (args.SaveReason)
        {
            case UserDataSaveReason.UpdateUserRating:
                _logger.LogDebug("Reason is user rating update, attempting favorite sync");
                HandleFavoriteSyncUsingMbid(data);
                return;
            case UserDataSaveReason.PlaybackFinished:
                _logger.LogDebug("Reason is playback finished, evaluating listen conditions");
                HandlePlaybackFinished(data);
                return;
            default:
                _logger.LogDebug("Dropping event - unsupported data save reason");
                return;
        }
    }

    /// <summary>
    /// Handle playback finished event (alternative recognition of listens).
    /// </summary>
    /// <param name="data">Event data.</param>
    private void HandlePlaybackFinished(EventData data)
    {
        var config = Plugin.GetConfiguration();
        if (!config.IsAlternativeModeEnabled)
        {
            _logger.LogDebug("Dropping event - alternative mode is disabled");
            return;
        }

        _logger.LogInformation(
            "Processing playback finished event for item {Item} associated with user {Username}",
            data.Item.Name,
            data.JellyfinUser.Username);

        UserConfig userConfig;
        try
        {
            AssertInAllowedLibrary(data.Item);
            userConfig = data.JellyfinUser.GetListenBrainzConfig();
            AssertSubmissionEnabled(userConfig);
            AssertBasicMetadataRequirements(data.Item);

            Monitor.Enter(_userDataSaveLock);
            EvaluateConditionsIfTracked(data.Item, data.JellyfinUser);
        }
        catch (Exception e)
        {
            _logger.LogInformation("Dropping event: {Reason}", e.Message);
            _logger.LogDebug(e, "Dropping event");
            return;
        }
        finally
        {
            Monitor.Exit(_userDataSaveLock);
        }

        _logger.LogDebug("All checks passed, preparing for sending listen");
        var now = DateUtils.CurrentTimestamp;

        AudioItemMetadata? metadata = null;
        try
        {
            AssertMusicBrainzIsEnabled();
            _logger.LogInformation("Getting additional metadata...");
            metadata = _musicBrainzClient.GetAudioItemMetadata(data.Item);
            _logger.LogInformation("Additional metadata successfully received");
        }
        catch (Exception e)
        {
            _logger.LogInformation("Additional metadata are not available: {Reason}", e.Message);
            _logger.LogDebug(e, "Additional metadata are not available");
        }

        if (config.IsBackupEnabled && userConfig.IsBackupEnabled)
        {
            try
            {
                _logger.LogInformation("Adding listen to backups...");
                _backupManager.Backup(userConfig.UserName, data.Item, metadata, now);
                _logger.LogInformation("Listen successfully backed up");
            }
            catch (Exception e)
            {
                _logger.LogInformation("Listen backup failed: {Reason}", e.Message);
                _logger.LogDebug(e, "Listen backup failed");
            }
        }

        try
        {
            _logger.LogInformation("Sending listen...");
            _listenBrainzClient.SendListen(userConfig, data.Item, metadata, now);
            _logger.LogInformation("Listen successfully sent");
        }
        catch (Exception e)
        {
            _logger.LogInformation("Failed to send listen: {Reason}", e.Message);
            _logger.LogDebug(e, "Failed to send listen");

            _listensCache.AddListen(data.JellyfinUser.Id, data.Item, metadata, now);
            _listensCache.Save();
            _logger.LogInformation("Listen has been added to the cache");
            return;
        }

        if (userConfig.IsFavoritesSyncEnabled)
        {
            HandleFavoriteSync(data, metadata, userConfig, now);
        }
    }

    private void HandleFavoriteSync(EventData data, AudioItemMetadata? metadata, UserConfig userConfig, long listenTs)
    {
        _logger.LogInformation(
            "Attempting to sync favorite status for track {Track} and user {User}",
            data.Item.Name,
            data.JellyfinUser.Username);

        try
        {
            var userItemData = _userDataManager.GetUserData(data.JellyfinUser, data.Item);
            if (metadata?.RecordingMbid is not null)
            {
                _logger.LogInformation("Recording MBID is available, using it for favorite sync");
                _listenBrainzClient.SendFeedback(userConfig, userItemData.IsFavorite, metadata.RecordingMbid);
                _logger.LogInformation("Favorite sync has been successful");
                return;
            }

            _logger.LogInformation("No recording MBID is available, will attempt to sync favorite status using MSID");
            SendFeedbackUsingMsid(userConfig, userItemData.IsFavorite, listenTs);
            _logger.LogInformation("Favorite sync has been successful");
        }
        catch (Exception e)
        {
            _logger.LogInformation("Favorite sync failed: {Reason}", e.Message);
            _logger.LogDebug(e, "Favorite sync failed");
        }
    }

    /// <summary>
    /// Handle favorite sync using MBID. Does not fall back to using MSID.
    /// </summary>
    /// <param name="data">Event data.</param>
    private void HandleFavoriteSyncUsingMbid(EventData data)
    {
        try
        {
            AssertImmediateFavoriteSyncIsEnabled();
            AssertMusicBrainzIsEnabled();

            _logger.LogDebug("Checking if favorite sync is enabled");
            var userConfig = data.JellyfinUser.GetListenBrainzConfig();
            if (!userConfig.IsFavoritesSyncEnabled)
            {
                _logger.LogDebug("Favorite sync is disabled");
                return;
            }

            _logger.LogDebug("Favorite sync is enabled");

            _logger.LogInformation("Getting additional metadata...");
            var metadata = _musicBrainzClient.GetAudioItemMetadata(data.Item);
            _logger.LogInformation("Additional metadata successfully received");

            var userItemData = _userDataManager.GetUserData(data.JellyfinUser, data.Item);
            if (string.IsNullOrEmpty(metadata.RecordingMbid))
            {
                _logger.LogInformation("No recording MBID is available, cannot sync favorite");
                return;
            }

            _logger.LogInformation("Attempting to sync favorite status");
            _listenBrainzClient.SendFeedback(userConfig, userItemData.IsFavorite, metadata.RecordingMbid);
            _logger.LogInformation("Favorite sync has been successful");
        }
        catch (Exception e)
        {
            _logger.LogInformation("Favorite sync failed: {Reason}", e.Message);
            _logger.LogDebug(e, "Favorite sync failed");
        }
    }

    /// <summary>
    /// Assert MusicBrainz integration is enabled.
    /// </summary>
    /// <exception cref="PluginException">MusicBrainz integration is not enabled.</exception>
    private void AssertMusicBrainzIsEnabled()
    {
        _logger.LogDebug("Checking if MusicBrainz integration is enabled");
        if (!Plugin.GetConfiguration().IsMusicBrainzEnabled)
        {
            throw new PluginException("MusicBrainz integration is disabled");
        }

        _logger.LogDebug("MusicBrainz integration is enabled");
    }

    /// <summary>
    /// Asser immediate favorite sync is enabled.
    /// </summary>
    /// <exception cref="PluginException">Immediate favorite sync is disabled.</exception>
    private void AssertImmediateFavoriteSyncIsEnabled()
    {
        _logger.LogDebug("Checking if immediate favorite sync is enabled");
        if (!Plugin.GetConfiguration().IsImmediateFavoriteSyncEnabled)
        {
            throw new PluginException("Immediate favorite sync is disabled");
        }

        _logger.LogDebug("Immediate favorite sync is enabled");
    }

    /// <summary>
    /// Verifies the event.
    /// </summary>
    /// <param name="eventArgs">Event arguments.</param>
    /// <returns>Event data.</returns>
    /// <exception cref="ArgumentException">Event data are not valid.</exception>
    private static EventData GetEventData(PlaybackProgressEventArgs eventArgs)
    {
        if (eventArgs.Item is not Audio item)
        {
            throw new ArgumentException("Event item is not an Audio item");
        }

        var jellyfinUser = eventArgs.Users.FirstOrDefault();
        if (jellyfinUser is null)
        {
            throw new ArgumentException("No user is associated with this event");
        }

        return new EventData { Item = item, JellyfinUser = jellyfinUser };
    }

    /// <summary>
    /// Verifies the event.
    /// </summary>
    /// <param name="eventArgs">Event arguments.</param>
    /// <returns>Event data.</returns>
    /// <exception cref="ArgumentException">Event data are not valid.</exception>
    private EventData GetEventData(UserDataSaveEventArgs eventArgs)
    {
        if (eventArgs.Item is not Audio item)
        {
            throw new ArgumentException("Event item is not an Audio item");
        }

        var jellyfinUser = _userManager.GetUserById(eventArgs.UserId);
        if (jellyfinUser is null)
        {
            throw new ArgumentException("No user is associated with this event");
        }

        return new EventData { Item = item, JellyfinUser = jellyfinUser };
    }

    /// <summary>
    /// Check if the item has basic metadata.
    /// </summary>
    /// <param name="item">Item to be checked.</param>
    /// <exception cref="PluginException">Item does not meet requirements.</exception>
    private void AssertBasicMetadataRequirements(Audio item)
    {
        _logger.LogDebug("Checking item basic metadata");
        try
        {
            item.AssertHasMetadata();
        }
        catch (Exception e)
        {
            throw new PluginException("Item metadata are not valid", e);
        }

        _logger.LogDebug("Item has basic metadata");
    }

    /// <summary>
    /// Assert user has enabled listen submission.
    /// </summary>
    /// <param name="config">User configuration.</param>
    /// <exception cref="PluginException">User has disabled listen submission.</exception>
    private void AssertSubmissionEnabled(UserConfig config)
    {
        _logger.LogDebug("Checking if ListenBrainz submission is enabled for the user");
        if (config.IsNotListenSubmitEnabled)
        {
            throw new PluginException("ListenBrainz submission is disabled for the user");
        }

        _logger.LogDebug("ListenBrainz submission is enabled for the user");
    }

    private void SendFeedbackUsingMsid(UserConfig userConfig, bool isFavorite, long listenTs)
    {
        const int MaxAttempts = 4;
        const int BackOffSecs = 5;
        var sleepSecs = 1;

        // TODO: Improve logging

        // Delay to maximize the chance of getting it on first try
        Thread.Sleep(500);
        string? recordingMsid = null;
        for (int i = 0; i < MaxAttempts; i++)
        {
            try
            {
                recordingMsid = _listenBrainzClient.GetRecordingMsidByListenTs(userConfig, listenTs);
                break;
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "Failed to get recording MSID");
            }

            sleepSecs *= BackOffSecs;
            sleepSecs += new Random().Next(20);
            _logger.LogDebug(
                "Recording MSID with listen timestamp {Ts} not found, will retry in {Secs} seconds",
                listenTs,
                sleepSecs);

            Thread.Sleep(sleepSecs * 1000);
        }

        if (recordingMsid is null)
        {
            throw new PluginException("Maximum retry attempts have been reached");
        }

        _listenBrainzClient.SendFeedback(userConfig, isFavorite, recordingMsid: recordingMsid);
    }

    /// <summary>
    /// Evaluate listen submit conditions if the played item is tracked.
    /// </summary>
    /// <param name="item">Item to be tracked.</param>
    /// <param name="user">User associated with the listen.</param>
    /// <exception cref="PluginException">Item is not tracked or tracking is not valid.</exception>
    private void EvaluateConditionsIfTracked(BaseItem item, User user)
    {
        var trackedItem = _playbackTracker.GetItem(user.Id.ToString(), item.Id.ToString());
        if (trackedItem is null)
        {
            _logger.LogDebug("Playback is not tracked for this item, assuming offline playback");
            return;
        }

        if (!trackedItem.IsValid)
        {
            throw new PluginException("Playback tracking is not valid for this item");
        }

        var delta = DateUtils.CurrentTimestamp - trackedItem.StartedAt;
        var deltaTicks = delta * TimeSpan.TicksPerSecond;
        var runtime = item.RunTimeTicks ?? 0;
        Limits.AssertSubmitConditions(deltaTicks, runtime);
        _playbackTracker.InvalidateItem(user.Id.ToString(), trackedItem);
    }

    /// <summary>
    /// Verifies if the specified item is in any allowed library.
    /// </summary>
    /// <param name="item">Item to verify.</param>
    /// <exception cref="PluginException">Item is not in any allowed library.</exception>
    private void AssertInAllowedLibrary(BaseItem item)
    {
        _logger.LogDebug("Checking if item is in any allowed libraries");
        var isInAllowed = _libraryManager
            .GetCollectionFolders(item)
            .Select(il => il.Id)
            .Intersect(GetAllowedLibraries())
            .Any();

        if (!isInAllowed)
        {
            throw new PluginException("Item is not in any allowed library");
        }

        _logger.LogDebug("Item is in at least one allowed library");
    }

    private IEnumerable<Guid> GetAllowedLibraries()
    {
        var allLibraries = Plugin.GetConfiguration().LibraryConfigs;
        if (allLibraries.Count > 0)
        {
            return allLibraries.Where(lc => lc.IsAllowed).Select(lc => lc.Id);
        }

        return _libraryManager.GetMusicLibraries().Select(ml => ml.Id);
    }

    private void AssertListenBrainzRequirements(PlaybackStopEventArgs args, EventData data)
    {
        _logger.LogDebug("Checking ListenBrainz requirements");
        var position = args.PlaybackPositionTicks;
        if (position is null)
        {
            throw new PluginException("Playback position is not set");
        }

        try
        {
            Limits.AssertSubmitConditions((long)position, data.Item.RunTimeTicks ?? 0);
        }
        catch (Exception e)
        {
            throw new PluginException("Requirements were not met", e);
        }

        _logger.LogDebug("Requirements were met");
    }

    private IDisposable? BeginLogScope()
    {
        var eventId = Guid.NewGuid().ToString("N")[..7];
        return _logger.BeginScope(new Dictionary<string, object> { { "EventId", eventId } });
    }

    private struct EventData
    {
        public Audio Item { get; init; }

        public User JellyfinUser { get; init; }
    }
}
