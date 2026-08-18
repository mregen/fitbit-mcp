# fitbit-mcp

A .NET-based Model Context Protocol (MCP) server for the [Google Health API](https://developers.google.com/health) (Fitbit and Pixel Watch data, including weight logged by a Fitbit Aria scale).

## Status

Early scaffolding. `get_weight_history` is implemented but not yet exercised against a live Google account — verify the `dataPoints:rollUp` request/response shape against the [discovery doc](https://health.googleapis.com/$discovery/rest?version=v4) before relying on it.

## Plan

- Built on the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.AspNetCore`), using Streamable HTTP transport (`MapMcp`).
- Auth: OAuth 2.0 authorization-code + PKCE flow against Google's endpoints, scoped to the Google Health API. Tokens are cached locally (in this project's `dotnet user-secrets` store) and refreshed automatically; nothing is committed to the repo.
- First use case: expose Google Health's weight data (sourced from a Fitbit Aria 2 scale) as `get_weight_history`, so it can be compared against [fatsecret-mcp](../fatsecret-mcp)'s `get_weight_history` / `add_weight_entry` tools in a live Claude session, to fill in FatSecret weight entries that are currently missing.
- The legacy Fitbit Web API (activity/sleep/heart rate/nutrition) is a later phase, not part of this scaffold.
- Target: `net10.0`.

## Setup

1. In a Google Cloud project, enable the Google Health API and create an OAuth 2.0 client (redirect URI `http://127.0.0.1:3000/callback`).
2. From `src/FitbitMcp`: `dotnet user-secrets set GoogleHealth:ClientId "<client id>"` and `dotnet user-secrets set GoogleHealth:ClientSecret "<client secret>"`.
3. `dotnet run --project src/FitbitMcp -- auth login` to complete the OAuth flow and cache tokens.
4. `dotnet run --project src/FitbitMcp` to start the MCP server (`POST /mcp`).

## License

MIT — see [LICENSE](LICENSE).
