// SPDX-License-Identifier: MIT

namespace FitbitMcp.Tests.TestSupport;

/// <summary>
/// Captures the last outgoing request and returns a canned response, so OAuth2 client tests can assert
/// on exactly what was sent (method, URL, form body) without hitting the real Google endpoints.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(responder(request));
    }
}
