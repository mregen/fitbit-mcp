# fitbit-mcp

[![Build](https://img.shields.io/github/actions/workflow/status/mregen/fitbit-mcp/build.yml?branch=main&label=build)](https://github.com/mregen/fitbit-mcp/actions/workflows/build.yml)

A .NET-based Model Context Protocol (MCP) server for the [Google Health API](https://developers.google.com/health) — the data a Fitbit tracker or Aria scale ends up in once it syncs through Google.

**This is an independent, community-built project - not an official Fitbit, Google, or Google Health product.** It is not affiliated with, endorsed by, or supported by Google or Fitbit. It works against the public Google Health API using your own OAuth client credentials, registered separately in your own Google Cloud project. "Fitbit" and "Google Health" are trademarks of their respective owners, used here only to describe API compatibility.

## Purpose

A Fitbit tracker or scale quietly builds up years of weight, activity, heart rate, and sleep data — but it all sits behind Google's own app, disconnected from everything else. This project connects that data to an LLM directly, so you can ask about it in plain language instead of scrolling through graphs, and — the reason this exists in the first place — compare it against other trackers like [FatSecret](../fatsecret-mcp) instead of re-entering the same numbers by hand.

A couple of examples of what that looks like once the tools are connected (illustrative - your own numbers will differ):

> **You:** How did I sleep this week compared to last?
>
> **Claude:** This week you averaged 6h 42m asleep per night across 6 nights logged, up from 6h 10m the week before. Wednesday was your most restless night (58 minutes awake); Sunday was your best, at 7h 15m asleep with only 12 minutes awake.

> **You:** Is my Fitbit weight actually making it into FatSecret?
>
> **Claude:** Checking both... Google Health has 12 weigh-ins this month from your scale, but only 3 are logged in FatSecret. Want me to add the other 9? One thing to know: FatSecret only accepts entries from the last 2 days, so this only works for recent gaps — anything older would need to be entered by hand in FatSecret's own app.

That second example is a real constraint this project ran into, not a hypothetical — see [Status](#status) below.

## Requirements

- **.NET 10 SDK** - to build and run the server (see [Install](#install) below; not yet published as a prebuilt package)
- **An MCP-capable LLM client** - Claude Code is documented below; other MCP clients that can spawn a local stdio process should work the same way
- **A Google account and a Google Cloud project** - with the Google Health API enabled and an OAuth 2.0 client of your own (see [Configure credentials](#configure-credentials) below)

## Status

Working prototype, verified against a real account.

| Feature | Status |
|---|---|
| Weight history | ✅ Working, verified live |
| Activity (steps, calories, active minutes) | ✅ Working, verified live |
| Heart rate | ✅ Working, verified live |
| Sleep | ✅ Working, verified live - naps and main sleep on the same night aren't distinguished yet ([issue #17](https://github.com/mregen/fitbit-mcp/issues/17)) |
| Syncing weight into FatSecret | ⚠️ Works only for recent gaps (last ~2 days) - FatSecret's own API refuses to backdate further, discovered live, not something this project can work around |
| Legacy Fitbit Web API (as an alternative to Google Health) | ⏳ Not built - Google Health already covers the same data |
| Hosting for more than one person | ⏳ Design sketch only - see [`docs/cloud-deployment.md`](docs/cloud-deployment.md) |

See [open issues](https://github.com/mregen/fitbit-mcp/issues) for the current roadmap.

## Available tools

| Tool | What it needs from Google | What you get |
|---|---|---|
| `get_weight_history` | `health_metrics_and_measurements.readonly` | Body-weight entries for a month |
| `get_activity_summary` | `activity_and_fitness.readonly` | Daily steps, calories, and active minutes for a date range (last 14 days by default) |
| `get_heart_rate_history` | `activity_and_fitness.readonly` | Daily average/min/max heart rate for a date range (last 14 days by default) |
| `get_sleep_history` | `sleep.readonly` | Sleep sessions for a month - time asleep, awake, and in bed |

**If you authorized before 2026-08-19**, re-run the one-time authorization below - the permissions this project asks for changed, and an old authorization will only unlock weight, not the other three.

## Install

Not yet published anywhere - build and run it from a clone of this repo:

```bash
git clone https://github.com/mregen/fitbit-mcp.git
cd fitbit-mcp
```

You'll also need a Google Cloud project with the Google Health API enabled, and an OAuth 2.0 client of your own - see the next section.

## Configure credentials

Credentials are stored locally via [.NET user-secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets), run from `src/FitbitMcp/` - never committed to the repo.

1. In your Google Cloud project: enable the **Google Health API**, then create an OAuth 2.0 client. Use the **Web application** type (not Desktop) and add `http://127.0.0.1:3000/callback` under Authorized redirect URIs, exactly as written.
2. Store the client id/secret:

```bash
cd src/FitbitMcp
dotnet user-secrets set GoogleHealth:ClientId "<client id>"
dotnet user-secrets set GoogleHealth:ClientSecret "<client secret>"
```

### One-time authorization

This grants the server access to your own Google Health data. It's interactive - a browser opens for you to approve access - and finishes automatically, no code to copy by hand:

```bash
cd src/FitbitMcp
dotnet run -- auth login
```

The resulting tokens are cached in the same `dotnet user-secrets` store and refreshed automatically after that - you only need to do this again if access is revoked, or if the permissions this project asks for ever change (see the note under [Available tools](#available-tools)).

## Run it

The server supports two transports, chosen at startup - **stdio by default**, or HTTP via a flag:

```bash
cd src/FitbitMcp

dotnet run --no-launch-profile                                    # stdio - for MCP clients that spawn the process directly
dotnet run --no-launch-profile -- --http --urls http://localhost:5230   # HTTP - a long-running server on a port
```

(`--no-launch-profile` matters for stdio: without it, `dotnet run` prints a startup message to the same channel the MCP protocol uses, which breaks strict clients - confirmed live.)

In HTTP mode the MCP endpoint is at `<url>/mcp`, e.g. `http://localhost:5230/mcp`.

## Configure Claude Code

A project-scoped [`.mcp.json`](.mcp.json) is checked into this repo - open the project in Claude Code and it'll prompt once to trust the `fitbit-mcp` server, no manual setup needed.

To register it manually instead:

```bash
claude mcp add fitbit-mcp -- dotnet run --project src/FitbitMcp --no-launch-profile
```

Or over HTTP, with the server already running:

```bash
claude mcp add --transport http fitbit-local-dev http://localhost:5230/mcp
```

Restart or reconnect your Claude Code session afterward - new MCP registrations aren't picked up mid-session.

Credentials aren't in `.mcp.json` itself - they come from this machine's `dotnet user-secrets` store, so this only works where the one-time authorization above has already been completed. A different machine needs its own.

## Building from source / contributing

See [`CLAUDE.md`](CLAUDE.md) in the repo for architecture notes, design decisions, and current session-by-session status - it's kept up to date as the project's technical reference.

## License

MIT — see [LICENSE](LICENSE).
