// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;

namespace FitbitMcp.Auth;

/// <summary>
/// Thin wrapper around the Google Health API (https://health.googleapis.com/v4), transparently
/// refreshing the stored OAuth2 access token when it has expired or a call comes back 401.
///
/// Request/response shapes are taken from the live discovery document
/// (https://health.googleapis.com/$discovery/rest?version=v4 - resources.users.dataTypes.dataPoints
/// rollUp/list, schemas RollUpDataPointsRequest/Response, RollupDataPoint, ListDataPointsResponse,
/// DataPoint), not the REST reference page, which turned out to document a different (nonexistent)
/// scope/shape for weight - confirmed live via an invalid_scope error during the OAuth consent flow,
/// then a real authenticated get_weight_history call. Steps/calories/active-minutes/heart-rate/sleep
/// have not yet been exercised against a live account - same "verify before trusting" caveat applies.
///
/// Google enforces a maximum 14-day range for heart-rate, active-minutes, total-calories, and
/// calories-in-heart-rate-zone rollups (90 days for other rollup types) - callers are responsible for
/// respecting that; this class doesn't validate it client-side.
/// </summary>
public sealed class GoogleHealthApi(HttpClient httpClient, GoogleHealthOAuth2Client oauthClient, TokenStore tokenStore)
{
    private const string BaseUrl = "https://health.googleapis.com/v4";

    public Task<string> GetRollupAsync(string dataTypeId, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
    {
        var startUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(end.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var body = new
        {
            range = new { startTime = startUtc.ToString("O"), endTime = endUtc.ToString("O") },
            windowSize = "86400s",
        };

        return SendAsync(HttpMethod.Post, $"users/me/dataTypes/{dataTypeId}/dataPoints:rollUp", body, cancellationToken);
    }

    /// <summary>
    /// Queries raw (non-rolled-up) data points - used for data types like sleep that don't have a
    /// rollup value type. See the "filter" parameter docs on dataPoints.list in the discovery doc for
    /// the (data-type-specific) filter expression syntax.
    /// </summary>
    public Task<string> ListDataPointsAsync(string dataTypeId, string filter, CancellationToken cancellationToken = default)
    {
        var path = $"users/me/dataTypes/{dataTypeId}/dataPoints?filter={Uri.EscapeDataString(filter)}";
        return SendAsync(HttpMethod.Get, path, body: null, cancellationToken);
    }

    private async Task<string> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var tokens = await GetValidTokensAsync(cancellationToken);
        var response = await IssueRequestAsync(method, path, body, tokens.AccessToken, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokens = await RefreshAndStoreAsync(tokens.RefreshToken, cancellationToken);
            response = await IssueRequestAsync(method, path, body, tokens.AccessToken, cancellationToken);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Google Health request failed ({(int)response.StatusCode}): {responseBody}");
        }

        return responseBody;
    }

    private Task<HttpResponseMessage> IssueRequestAsync(HttpMethod method, string path, object? body, string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, $"{BaseUrl}/{path}");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<StoredTokens> GetValidTokensAsync(CancellationToken cancellationToken)
    {
        var tokens = tokenStore.Load()
            ?? throw new InvalidOperationException("No Google Health tokens found. Run 'dotnet run -- auth login' first.");

        return tokens.ExpiresAtUtc <= DateTimeOffset.UtcNow
            ? await RefreshAndStoreAsync(tokens.RefreshToken, cancellationToken)
            : tokens;
    }

    private async Task<StoredTokens> RefreshAndStoreAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var response = await oauthClient.RefreshAsync(refreshToken, cancellationToken);
        var stored = new StoredTokens(response.AccessToken, response.RefreshToken ?? refreshToken, DateTimeOffset.UtcNow.AddSeconds(response.ExpiresInSeconds));
        tokenStore.Save(stored);
        return stored;
    }
}
