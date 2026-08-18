// SPDX-License-Identifier: MIT

using System.Net;
using System.Net.Http.Headers;

namespace FitbitMcp.Auth;

/// <summary>
/// Thin wrapper around the Google Health API (https://health.googleapis.com/v4), transparently
/// refreshing the stored OAuth2 access token when it has expired or a call comes back 401.
///
/// Request/response shape for dataPoints:rollUp is taken from the live discovery document
/// (https://health.googleapis.com/$discovery/rest?version=v4 - resources.users.dataTypes.dataPoints.rollUp,
/// schemas RollUpDataPointsRequest/Response, RollupDataPoint, WeightRollupValue), not the REST reference
/// page, which turned out to document a different (nonexistent) scope name and shape - confirmed live via
/// an invalid_scope error during the OAuth consent flow. Still not exercised against a real authenticated
/// response, so double-check here first if that ever comes back oddly shaped.
/// </summary>
public sealed class GoogleHealthApi(HttpClient httpClient, GoogleHealthOAuth2Client oauthClient, TokenStore tokenStore)
{
    private const string BaseUrl = "https://health.googleapis.com/v4";

    public Task<string> GetWeightRollupAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
    {
        var startUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(end.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var body = new
        {
            range = new { startTime = startUtc.ToString("O"), endTime = endUtc.ToString("O") },
            windowSize = "86400s",
        };

        return SendAsync("users/me/dataTypes/weight/dataPoints:rollUp", body, cancellationToken);
    }

    private async Task<string> SendAsync(string path, object body, CancellationToken cancellationToken)
    {
        var tokens = await GetValidTokensAsync(cancellationToken);
        var response = await PostAsync(path, body, tokens.AccessToken, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokens = await RefreshAndStoreAsync(tokens.RefreshToken, cancellationToken);
            response = await PostAsync(path, body, tokens.AccessToken, cancellationToken);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Google Health request failed ({(int)response.StatusCode}): {responseBody}");
        }

        return responseBody;
    }

    private Task<HttpResponseMessage> PostAsync(string path, object body, string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{path}") { Content = JsonContent.Create(body) };
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
