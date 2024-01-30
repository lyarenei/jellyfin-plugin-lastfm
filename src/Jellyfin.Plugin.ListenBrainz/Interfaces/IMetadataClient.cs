using Jellyfin.Plugin.ListenBrainz.Dtos;
using MediaBrowser.Controller.Entities.Audio;

namespace Jellyfin.Plugin.ListenBrainz.Interfaces;

/// <summary>
/// MusicBrainz client.
/// </summary>
public interface IMetadataClient
{
    /// <summary>
    /// Get additional metadata from MusicBrainz for specified audio item.
    /// </summary>
    /// <param name="item">Audio item.</param>
    /// <returns>MusicBrainz recording.</returns>
    public Task<AudioItemMetadata> GetAudioItemMetadataAsync(Audio item);

    /// <summary>
    /// Get additional metadata for specified audio item.
    /// </summary>
    /// <param name="item">Audio item.</param>
    /// <returns>Audio item metadata.</returns>
    public AudioItemMetadata GetAudioItemMetadata(Audio item);
}
