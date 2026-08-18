// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using FitbitMcp.Auth;
using FitbitMcp.Tests.TestSupport;

namespace FitbitMcp.Tests;

[TestFixture]
public class GoogleHealthOAuth2ClientTests
{
    [Test]
    public void BuildAuthorizeUrl_IncludesPkceAndClientParameters()
    {
        var client = new GoogleHealthOAuth2Client(new HttpClient(), "test-client-id", "test-client-secret");

        var url = client.BuildAuthorizeUrl("test-state", "test-challenge");

        Assert.That(url, Does.StartWith("https://accounts.google.com/o/oauth2/v2/auth?"));
        Assert.That(url, Does.Contain("client_id=test-client-id"));
        Assert.That(url, Does.Contain("code_challenge=test-challenge"));
        Assert.That(url, Does.Contain("code_challenge_method=S256"));
        Assert.That(url, Does.Contain($"redirect_uri={Uri.EscapeDataString(GoogleHealthOAuth2Client.RedirectUri)}"));
    }

    [Test]
    public async Task ExchangeCodeAsync_PostsAuthorizationCodeGrant_AndParsesTokens()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"access-123","refresh_token":"refresh-456","expires_in":3600}""",
                Encoding.UTF8, "application/json"),
        });
        var client = new GoogleHealthOAuth2Client(new HttpClient(handler), "test-client-id", "test-client-secret");

        var tokens = await client.ExchangeCodeAsync("auth-code", "code-verifier");

        Assert.That(tokens.AccessToken, Is.EqualTo("access-123"));
        Assert.That(tokens.RefreshToken, Is.EqualTo("refresh-456"));
        Assert.That(tokens.ExpiresInSeconds, Is.EqualTo(3600));
        Assert.That(handler.LastRequest?.RequestUri?.ToString(), Is.EqualTo("https://oauth2.googleapis.com/token"));

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("grant_type=authorization_code"));
        Assert.That(body, Does.Contain("code=auth-code"));
        Assert.That(body, Does.Contain("code_verifier=code-verifier"));
    }

    [Test]
    public async Task RefreshAsync_PostsRefreshTokenGrant()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"access_token":"access-789","expires_in":3600}""",
                Encoding.UTF8, "application/json"),
        });
        var client = new GoogleHealthOAuth2Client(new HttpClient(handler), "test-client-id", "test-client-secret");

        var tokens = await client.RefreshAsync("refresh-456");

        Assert.That(tokens.AccessToken, Is.EqualTo("access-789"));
        Assert.That(tokens.RefreshToken, Is.Null);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.That(body, Does.Contain("grant_type=refresh_token"));
        Assert.That(body, Does.Contain("refresh_token=refresh-456"));
    }

    [Test]
    public void ExchangeCodeAsync_ThrowsOnErrorResponse()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json"),
        });
        var client = new GoogleHealthOAuth2Client(new HttpClient(handler), "test-client-id", "test-client-secret");

        Assert.That(async () => await client.ExchangeCodeAsync("bad-code", "verifier"), Throws.TypeOf<HttpRequestException>());
    }
}
