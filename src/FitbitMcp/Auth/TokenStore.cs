// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace FitbitMcp.Auth;

public sealed record StoredTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Reads/writes Google Health OAuth2 tokens from the same secrets.json file `dotnet user-secrets`
/// already manages for this project's UserSecretsId, rather than introducing a separate dotfile
/// convention. Reading goes through IConfiguration (consistent with how the rest of the app reads
/// config); writing has to touch the backing file directly since IConfiguration is read-only.
/// </summary>
public sealed class TokenStore(IConfiguration configuration)
{
    public StoredTokens? Load()
    {
        var accessToken = configuration["GoogleHealth:AccessToken"];
        var refreshToken = configuration["GoogleHealth:RefreshToken"];
        var expiresAt = configuration["GoogleHealth:ExpiresAtUtc"];
        if (accessToken is null || refreshToken is null || expiresAt is null)
        {
            return null;
        }

        return new StoredTokens(accessToken, refreshToken, DateTimeOffset.Parse(expiresAt, CultureInfo.InvariantCulture));
    }

    public void Save(StoredTokens tokens)
    {
        var path = GetSecretsFilePath();
        var json = File.Exists(path) ? File.ReadAllText(path) : "{}";
        var root = JsonNode.Parse(json)?.AsObject() ?? [];

        root["GoogleHealth:AccessToken"] = tokens.AccessToken;
        root["GoogleHealth:RefreshToken"] = tokens.RefreshToken;
        root["GoogleHealth:ExpiresAtUtc"] = tokens.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string GetSecretsFilePath()
    {
        var userSecretsId = typeof(TokenStore).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId
            ?? throw new InvalidOperationException("UserSecretsId is not configured for this assembly.");

        var profileRoot = Environment.GetFolderPath(
            OperatingSystem.IsWindows() ? Environment.SpecialFolder.ApplicationData : Environment.SpecialFolder.UserProfile);
        var secretsRoot = OperatingSystem.IsWindows()
            ? Path.Combine(profileRoot, "Microsoft", "UserSecrets")
            : Path.Combine(profileRoot, ".microsoft", "usersecrets");

        return Path.Combine(secretsRoot, userSecretsId, "secrets.json");
    }
}
