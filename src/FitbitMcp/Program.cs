// SPDX-License-Identifier: MIT

using FitbitMcp.Auth;

var builder = WebApplication.CreateBuilder(args);

if (args.Length > 0 && args[0] == "auth")
{
    await AuthCli.RunAsync(args[1..], builder.Configuration);
    return;
}

builder.Services.AddHttpClient();
builder.Services.AddSingleton(sp => {
    var config = sp.GetRequiredService<IConfiguration>();
    var clientId = config["GoogleHealth:ClientId"]
        ?? throw new InvalidOperationException("GoogleHealth:ClientId is not configured.");
    var clientSecret = config["GoogleHealth:ClientSecret"]
        ?? throw new InvalidOperationException("GoogleHealth:ClientSecret is not configured.");
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleHealthOAuth2Client));
    return new GoogleHealthOAuth2Client(httpClient, clientId, clientSecret);
});
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton(sp => {
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleHealthApi));
    return new GoogleHealthApi(httpClient, sp.GetRequiredService<GoogleHealthOAuth2Client>(), sp.GetRequiredService<TokenStore>());
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");

app.Run();
