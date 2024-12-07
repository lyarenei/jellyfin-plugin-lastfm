using Jellyfin.Plugin.ListenBrainz.Dtos;
using Jellyfin.Plugin.ListenBrainz.Exceptions;
using MediaBrowser.Controller.Entities.Audio;

namespace Jellyfin.Plugin.ListenBrainz.Interfaces;

/// <summary>
/// Backup manager interface.
/// </summary>
public interface IBackupManager : IDisposable
{
    /// <summary>
    /// Back up listen of a current item.
    /// </summary>
    /// <param name="userName">ListenBrainz username.</param>
    /// <param name="item">Listened item.</param>
    /// <param name="metadata">Item metadata.</param>
    /// <param name="timestamp">Listen timestamp.</param>
    /// <exception cref="PluginException">Backup failed.</exception>
    public void Backup(string userName, Audio item, AudioItemMetadata? metadata, long timestamp);
}
