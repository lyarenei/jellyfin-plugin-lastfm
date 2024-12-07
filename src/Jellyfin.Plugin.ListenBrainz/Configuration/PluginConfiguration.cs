using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ListenBrainz.Configuration;

/// <summary>
/// ListenBrainz plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    private string? _musicBrainzUrlOverride;
    private string? _listenBrainzUrlOverride;
    private bool? _isMusicBrainzEnabledOverride;
    private bool? _isAlternativeModeEnabled;
    private bool? _isImmediateFavoriteSyncEnabled;
    private bool? _shouldEmitUserRatingEvent;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        UserConfigs = new Collection<UserConfig>();
        LibraryConfigs = new Collection<LibraryConfig>();
        BackupPath = string.Empty;
    }

    /// <summary>
    /// Gets or sets ListenBrainz API base URL.
    /// </summary>
    public string ListenBrainzApiUrl
    {
        get => _listenBrainzUrlOverride ?? Api.Resources.General.BaseUrl;
        set => _listenBrainzUrlOverride = value;
    }

    /// <summary>
    /// Gets a default ListenBrainz API base URL.
    /// </summary>
    [XmlIgnore]
    public string DefaultListenBrainzApiUrl => Api.Resources.General.BaseUrl;

    /// <summary>
    /// Gets or sets MusicBrainz API base URL.
    /// </summary>
    public string MusicBrainzApiUrl
    {
        get => _musicBrainzUrlOverride ?? MusicBrainzApi.Resources.Api.BaseUrl;
        set => _musicBrainzUrlOverride = value;
    }

    /// <summary>
    /// Gets a default MusicBrainz API base URL.
    /// </summary>
    [XmlIgnore]
    public string DefaultMusicBrainzApiUrl => MusicBrainzApi.Resources.Api.BaseUrl;

    /// <summary>
    /// Gets or sets a value indicating whether MusicBrainz integration is enabled.
    /// </summary>
    public bool IsMusicBrainzEnabled
    {
        get => _isMusicBrainzEnabledOverride ?? true;
        set => _isMusicBrainzEnabledOverride = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether alternative plugin mode is enabled.
    /// </summary>
    public bool IsAlternativeModeEnabled
    {
        get => _isAlternativeModeEnabled ?? false;
        set => _isAlternativeModeEnabled = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether immediate favorite sync is enabled.
    /// </summary>
    public bool IsImmediateFavoriteSyncEnabled
    {
        get => _isImmediateFavoriteSyncEnabled ?? true;
        set => _isImmediateFavoriteSyncEnabled = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin should emit
    /// <see cref="UserDataSaveReason.UpdateUserRating"/> event when syncing favorites from ListenBrainz.
    /// This is useful if there are other plugins used by the server which react on this event.
    /// </summary>
    public bool ShouldEmitUserRatingEvent
    {
        get => _shouldEmitUserRatingEvent ?? false;
        set => _shouldEmitUserRatingEvent = value;
    }

    /// <summary>
    /// Gets or sets ListenBrainz user configurations.
    /// </summary>
    [SuppressMessage("Warning", "CA2227", Justification = "Needed for deserialization")]
    public Collection<UserConfig> UserConfigs { get; set; }

    /// <summary>
    /// Gets or sets configurations of Jellyfin libraries for this plugin.
    /// </summary>
    [SuppressMessage("Warning", "CA2227", Justification = "Needed for deserialization")]
    public Collection<LibraryConfig> LibraryConfigs { get; set; }

    /// <summary>
    /// Gets or sets backup path.
    /// </summary>
    public string BackupPath { get; set; }

    /// <summary>
    /// Gets a value indicating whether backup feature is enabled.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public bool IsBackupEnabled => !string.IsNullOrEmpty(BackupPath);
}
