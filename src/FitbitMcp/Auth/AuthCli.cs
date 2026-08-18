// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace FitbitMcp.Auth;

/// <summary>
/// One-time interactive helper for completing the Google OAuth 2.0 authorization-code + PKCE flow.
/// Unlike FatSecret's OAuth1 oob/PIN flow, Google requires a redirect URI, so this runs a short-lived
/// loopback listener to catch the callback instead of asking the user to paste a code by hand.
///
/// Usage:
///   dotnet run -- auth login
/// </summary>
internal static class AuthCli
{
    public static async Task RunAsync(string[] args, IConfiguration configuration)
    {
        if (args.Length == 0 || args[0] != "login")
        {
            Console.Error.WriteLine("Usage: auth login");
            return;
        }

        var clientId = configuration["GoogleHealth:ClientId"]
            ?? throw new InvalidOperationException("GoogleHealth:ClientId is not configured.");
        var clientSecret = configuration["GoogleHealth:ClientSecret"]
            ?? throw new InvalidOperationException("GoogleHealth:ClientSecret is not configured.");

        using var httpClient = new HttpClient();
        var client = new GoogleHealthOAuth2Client(httpClient, clientId, clientSecret);

        var state = ToUrlSafeBase64(RandomNumberGenerator.GetBytes(16));
        var codeVerifier = ToUrlSafeBase64(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = ToUrlSafeBase64(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        using var listener = new HttpListener();
        var prefix = GoogleHealthOAuth2Client.RedirectUri.EndsWith('/')
            ? GoogleHealthOAuth2Client.RedirectUri
            : GoogleHealthOAuth2Client.RedirectUri + "/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var authorizeUrl = client.BuildAuthorizeUrl(state, codeChallenge);
        Console.WriteLine($"Opening browser for consent:\n{authorizeUrl}");
        TryOpenBrowser(authorizeUrl);

        var context = await listener.GetContextAsync();
        var query = context.Request.QueryString;
        var returnedState = query["state"];
        var code = query["code"];

        var responseBody = Encoding.UTF8.GetBytes("You can close this window and return to the terminal.");
        context.Response.ContentType = "text/plain";
        await context.Response.OutputStream.WriteAsync(responseBody);
        context.Response.Close();
        listener.Stop();

        if (returnedState != state || code is null)
        {
            Console.Error.WriteLine("OAuth callback did not return the expected state/code.");
            return;
        }

        var tokens = await client.ExchangeCodeAsync(code, codeVerifier);
        var refreshToken = tokens.RefreshToken ?? throw new InvalidOperationException(
            "Google did not return a refresh token. Revoke prior access at https://myaccount.google.com/permissions and retry.");

        var tokenStore = new TokenStore(configuration);
        tokenStore.Save(new StoredTokens(tokens.AccessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresInSeconds)));

        Console.WriteLine("Google Health tokens saved.");
    }

    private static string ToUrlSafeBase64(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"Could not open a browser automatically ({ex.Message}). Open this URL manually:\n{url}");
        }
    }
}
