using Jellyfin.Plugin.ListenBrainz.Managers;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ClientUtils = Jellyfin.Plugin.ListenBrainz.Clients.Utils;

namespace Jellyfin.Plugin.ListenBrainz;

/// <summary>
/// ListenBrainz plugin service for Jellyfin server.
/// </summary>
public sealed class PluginService : IHostedService, IDisposable
{
    private readonly ILogger<PluginService> _logger;
    private readonly ISessionManager _sessionManager;
    private readonly IUserDataManager _userDataManager;
    private readonly PluginImplementation _plugin;
    private bool _isActive;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginService"/> class.
    /// </summary>
    /// <param name="sessionManager">Session manager.</param>
    /// <param name="loggerFactory">Logger factory.</param>
    /// <param name="clientFactory">HTTP client factory.</param>
    /// <param name="userDataManager">User data manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="userManager">User manager.</param>
    public PluginService(
        ISessionManager sessionManager,
        ILoggerFactory loggerFactory,
        IHttpClientFactory clientFactory,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        IUserManager userManager)
    {
        _logger = loggerFactory.CreateLogger<PluginService>();
        _sessionManager = sessionManager;
        _userDataManager = userDataManager;
        _isActive = false;

        var listenBrainzLogger = loggerFactory.CreateLogger(Plugin.LoggerCategory + ".ListenBrainzApi");
        var listenBrainzClient = ClientUtils.GetListenBrainzClient(listenBrainzLogger, clientFactory, libraryManager);

        var musicBrainzLogger = loggerFactory.CreateLogger(Plugin.LoggerCategory + ".MusicBrainzApi");
        var musicBrainzClient = ClientUtils.GetMusicBrainzClient(musicBrainzLogger, clientFactory);

        var backupLogger = loggerFactory.CreateLogger(Plugin.LoggerCategory + ".Backup");
        var backupManager = new BackupManager(backupLogger);

        _plugin = new PluginImplementation(
            loggerFactory.CreateLogger(Plugin.LoggerCategory),
            listenBrainzClient,
            musicBrainzClient,
            userDataManager,
            userManager,
            libraryManager,
            backupManager);
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="PluginService"/> class.
    /// </summary>
    ~PluginService() => Dispose(false);

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
            _plugin.Dispose();
        }

        _isDisposed = true;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Activating plugin service");
        if (_isActive)
        {
            _logger.LogInformation("Plugin service has been already activated");
            return Task.CompletedTask;
        }

        _sessionManager.PlaybackStart += _plugin.OnPlaybackStart;
        _sessionManager.PlaybackStopped += _plugin.OnPlaybackStop;
        _userDataManager.UserDataSaved += _plugin.OnUserDataSave;

        _isActive = true;
        _logger.LogInformation("Plugin service in now active");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deactivating plugin service");
        if (!_isActive)
        {
            _logger.LogInformation("Plugin service has been already deactivated");
            return Task.CompletedTask;
        }

        _sessionManager.PlaybackStart -= _plugin.OnPlaybackStart;
        _sessionManager.PlaybackStopped -= _plugin.OnPlaybackStop;
        _userDataManager.UserDataSaved -= _plugin.OnUserDataSave;

        _isActive = false;
        _logger.LogInformation("Plugin service has been deactivated");
        return Task.CompletedTask;
    }
}
