using ListenBrainzPlugin.Configuration;
using ListenBrainzPlugin.Dtos;
using ListenBrainzPlugin.Extensions;
using ListenBrainzPlugin.Interfaces;
using ListenBrainzPlugin.ListenBrainzApi.Interfaces;
using ListenBrainzPlugin.ListenBrainzApi.Models.Requests;
using ListenBrainzPlugin.ListenBrainzApi.Resources;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.Extensions.Logging;

namespace ListenBrainzPlugin.Clients;

/// <summary>
/// ListenBrainz client for plugin.
/// </summary>
public class ListenBrainzClient : IListenBrainzClient
{
    private readonly ILogger _logger;
    private readonly IListenBrainzApiClient _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListenBrainzClient"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="apiClient">ListenBrainz API client instance.</param>
    public ListenBrainzClient(ILogger logger, IListenBrainzApiClient apiClient)
    {
        _logger = logger;
        _apiClient = apiClient;
    }

    /// <inheritdoc />
    public void SendNowPlaying(ListenBrainzUserConfig config, Audio item, AudioItemMetadata? audioMetadata)
    {
        var request = new SubmitListensRequest
        {
            ApiToken = config.ApiToken,
            ListenType = ListenType.PlayingNow,
            Payload = new[] { item.AsListen(itemMetadata: audioMetadata) }
        };

        _apiClient.SubmitListens(request, CancellationToken.None);
    }
}