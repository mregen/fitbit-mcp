// SPDX-License-Identifier: MIT

namespace FitbitMcp.Auth;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGoogleHealthClients(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton(sp => {
            var config = sp.GetRequiredService<IConfiguration>();
            var clientId = config["GoogleHealth:ClientId"]
                ?? throw new InvalidOperationException("GoogleHealth:ClientId is not configured.");
            var clientSecret = config["GoogleHealth:ClientSecret"]
                ?? throw new InvalidOperationException("GoogleHealth:ClientSecret is not configured.");
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleHealthOAuth2Client));
            return new GoogleHealthOAuth2Client(httpClient, clientId, clientSecret);
        });
        services.AddSingleton<TokenStore>();
        services.AddSingleton(sp => {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleHealthApi));
            return new GoogleHealthApi(httpClient, sp.GetRequiredService<GoogleHealthOAuth2Client>(), sp.GetRequiredService<TokenStore>());
        });

        return services;
    }
}
