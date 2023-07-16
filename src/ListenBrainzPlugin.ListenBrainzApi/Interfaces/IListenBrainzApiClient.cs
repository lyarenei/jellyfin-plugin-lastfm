using ListenBrainzPlugin.ListenBrainzApi.Models.Requests;
using ListenBrainzPlugin.ListenBrainzApi.Models.Responses;

namespace ListenBrainzPlugin.ListenBrainzApi.Interfaces;

/// <summary>
/// ListenBrainz API client.
/// </summary>
public interface IListenBrainzApiClient
{
    /// <summary>
    /// Validate provided token.
    /// </summary>
    /// <param name="request">Validate token request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Request response.</returns>
    public Task<ValidateTokenResponse?> ValidateToken(ValidateTokenRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Submit listens.
    /// </summary>
    /// <param name="request">Submit listens request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Request response.</returns>
    public Task<SubmitListensResponse?> SubmitListens(SubmitListensRequest request, CancellationToken cancellationToken);
}