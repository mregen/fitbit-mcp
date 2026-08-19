# fitbit-mcp

A .NET-based Model Context Protocol (MCP) server for the [Google Health API](https://developers.google.com/health) (Fitbit and Pixel Watch data, including weight logged by a Fitbit Aria scale).

## Status

Working prototype. `get_weight_history` is verified end to end against a real Google Health account. Compared against [fatsecret-mcp](../fatsecret-mcp) for a real month: most weight entries were missing from FatSecret, and backfilling them failed — not due to a bug here, but because FatSecret's `weight.update` API rejects any date more than 2 days in the past (undocumented, discovered live). **Practical implication**: this can sync weight going forward (run every day or two) but cannot backfill historical gaps — those need manual entry in FatSecret's own app. See [open issues](https://github.com/mregen/fitbit-mcp/issues) for what's left.

## Available tools

| Tool | Auth needed | Notes |
|---|---|---|
| `get_weight_history` | Google OAuth 2.0 (`health_metrics_and_measurements.readonly`) | Body-weight entries for a month, normalized to `{ date, weightKg }`; verified live against a real account |
| `get_activity_summary` | Google OAuth 2.0 (`activity_and_fitness.readonly`) | Daily steps/calories/active minutes for a date range (defaults to last 14 days — Google caps calories/active-minutes queries at 14 days), normalized to `{ date, steps, totalCalories, activeMinutes }`; not yet exercised against a live account |
| `get_heart_rate_history` | Google OAuth 2.0 (`activity_and_fitness.readonly`) | Daily avg/min/max heart rate for a date range (defaults to last 14 days, same 14-day cap), normalized to `{ date, avgBpm, minBpm, maxBpm }`; not yet exercised against a live account |
| `get_sleep_history` | Google OAuth 2.0 (`sleep.readonly`) | Sleep sessions for a month, normalized to `{ date, minutesAsleep, minutesAwake, minutesInBed }` (date = wake-up date; capped at 25 sessions per call by Google); not yet exercised against a live account |

**If you authorized before 2026-08-19**, the OAuth scope changed — re-run `auth login` (step 2 below) to pick up `activity_and_fitness.readonly` and `sleep.readonly`. Calling the new tools with an old token fails with a clear `MISSING_OAUTH_SCOPE` error from Google, not a crash.

## Running locally

### Prerequisites

- .NET 10 SDK
- A Google Cloud project with the Google Health API enabled and an OAuth 2.0 client configured

### 1. Configure credentials

Credentials are read from [.NET user-secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets), run from `src/FitbitMcp/` — never commit them to the repo.

1. In the Google Cloud project, enable the Google Health API and create an OAuth 2.0 client (redirect URI `http://127.0.0.1:3000/callback`).
2. Store the credentials:

```bash
cd src/FitbitMcp
dotnet user-secrets set GoogleHealth:ClientId "<client id>"
dotnet user-secrets set GoogleHealth:ClientSecret "<client secret>"
```

### 2. Complete the OAuth 2.0 flow (one time)

This grants the server access to your own Google Health data. It's interactive — a browser opens for consent — and completes automatically via a short-lived local callback listener (no PIN to copy).

```bash
cd src/FitbitMcp
dotnet run -- auth login
```

Tokens (access + refresh) are cached in this project's `dotnet user-secrets` store and refreshed automatically afterward — you only need to do this again if access is revoked.

### 3. Run the server

The server supports two transports, chosen at startup — **stdio by default**, or HTTP via a flag:

```bash
cd src/FitbitMcp

# stdio (default) - for MCP clients that spawn the process directly
dotnet run

# HTTP - a long-running server on a port, using ASP.NET Core's standard --urls flag
dotnet run -- --http --urls http://localhost:5230
```

In HTTP mode the MCP endpoint is at `<url>/mcp` (Streamable HTTP), e.g. `http://localhost:5230/mcp`.

### 4. Point an MCP client at it

**Claude Code**: a project-scoped [`.mcp.json`](.mcp.json) is checked into this repo — open the project in Claude Code and it'll prompt (once) to trust the `fitbit-mcp` server, no manual registration needed.

To register manually instead (or with another MCP client), stdio (default transport):

```bash
claude mcp add fitbit-mcp -- dotnet run --project src/FitbitMcp --no-launch-profile
```

`--no-launch-profile` matters here: without it, `dotnet run` prints a "Using launch settings..." preamble to stdout before the app starts, which corrupts the JSON-RPC stream for a strict-parsing client (confirmed live — the first attempt at `.mcp.json` had this bug).

Or HTTP, with the server already running from step 3:

```bash
claude mcp add --transport http fitbit-local-dev http://localhost:5230/mcp
```

Then restart or reconnect your Claude Code session — new MCP registrations aren't picked up mid-session — and all four tools become available.

**Note**: credentials aren't in `.mcp.json` at all — they're read from this machine's `dotnet user-secrets` store (see step 1/2 above), so this only works where `auth login` has already been completed. A different machine/environment needs its own OAuth setup first.

## Install as a .NET tool

Instead of running from source, package the server as an installable global tool:

```bash
dotnet pack src/FitbitMcp/FitbitMcp.csproj -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg FitbitMcp
```

This installs a `fitbit-mcp` command (credentials/auth setup above still apply — it reads the same user-secrets). Run it the same way as `dotnet run`:

```bash
fitbit-mcp                                       # stdio (default)
fitbit-mcp --http --urls http://localhost:5230    # HTTP
fitbit-mcp auth login                             # one-time OAuth2 flow
```

For Claude Code, point it at the installed command instead of `dotnet run`:

```bash
claude mcp add fitbit-mcp -- fitbit-mcp
```

## Plan / architecture

- Built on the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.AspNetCore`). Two transports, chosen at startup by branching on `args` before the host is built: stdio (default, `Host.CreateApplicationBuilder` + `.WithStdioServerTransport()`) or HTTP (`--http`, `WebApplication.CreateBuilder` + stateless Streamable HTTP via `MapMcp`) — the SDK doesn't support registering both on one builder, so `Program.cs` picks one.
- Also packable as a .NET global tool (`PackAsTool`, see above) as an alternative to (not-yet-built) Docker.
- Auth: OAuth 2.0 authorization-code + PKCE flow against Google's endpoints, scoped to the Google Health API. Tokens are cached locally (in this project's `dotnet user-secrets` store) and refreshed automatically; nothing is committed to the repo.
- First use case: expose Google Health's weight data (sourced from a Fitbit Aria 2 scale) as `get_weight_history`, so it can be compared against [fatsecret-mcp](../fatsecret-mcp)'s `get_weight_history` / `add_weight_entry` tools in a live Claude session, to fill in FatSecret weight entries that are currently missing.
- The legacy Fitbit Web API (activity/sleep/heart rate/nutrition) is a later phase, not part of this scaffold.
- Target: `net10.0`, single project for now, no container yet.

## License

MIT — see [LICENSE](LICENSE).
