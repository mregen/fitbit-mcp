// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;

namespace FitbitMcp.Auth;

/// <summary>
/// OAuth 2.0 authorization-code + PKCE flow (RFC 6749 / RFC 7636) against Google's endpoints,
/// used to obtain and refresh tokens scoped to the Google Health API
/// (https://developers.google.com/health).
/// </summary>
public sealed class GoogleHealthOAuth2Client(HttpClient httpClient, string clientId, string clientSecret)
{
    private const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    public const string RedirectUri = "http://127.0.0.1:3000/callback";
    public const string Scope =
        "https://www.googleapis.com/auth/googlehealth.health_metrics_and_measurements.readonly " +
        "https://www.googleapis.com/auth/googlehealth.activity_and_fitness.readonly " +
        "https://www.googleapis.com/auth/googlehealth.sleep.readonly " +
        "https://www.googleapis.com/auth/googlehealth.location.readonly";

    public string BuildAuthorizeUrl(string state, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };

        return $"{AuthorizeUrl}?{ToQueryString(query)}";
    }

    public Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken = default) =>
        PostFormAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code_verifier"] = codeVerifier,
        }, cancellationToken);

    public Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        PostFormAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        }, cancellationToken);

    private async Task<TokenResponse> PostFormAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(parameters);
        var response = await httpClient.PostAsync(TokenUrl, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Google token request failed ({(int)response.StatusCode}): {body}");
        }

        return JsonSerializer.Deserialize<TokenResponse>(body)
            ?? throw new InvalidOperationException("Google token response could not be parsed.");
    }

    private static string ToQueryString(IReadOnlyDictionary<string, string> parameters) =>
        string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
}

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresInSeconds);
