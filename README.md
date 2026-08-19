# fitbit-mcp

[![Build](https://img.shields.io/github/actions/workflow/status/mregen/fitbit-mcp/build.yml?branch=main&label=build)](https://github.com/mregen/fitbit-mcp/actions/workflows/build.yml)
[![NuGet Downloads](https://img.shields.io/nuget/dt/FitbitMcp?label=downloads)](https://www.nuget.org/packages/FitbitMcp)

A .NET-based Model Context Protocol (MCP) server for the [Google Health API](https://developers.google.com/health), installable as a .NET tool from [nuget.org](https://www.nuget.org/packages/FitbitMcp) — the data a Fitbit tracker or Aria scale ends up in once it syncs through Google.

**This is an independent, community-built project - not an official Fitbit, Google, or Google Health product.** It is not affiliated with, endorsed by, or supported by Google or Fitbit. It works against the public Google Health API using your own OAuth client credentials, registered separately in your own Google Cloud project. "Fitbit" and "Google Health" are trademarks of their respective owners, used here only to describe API compatibility.

## Purpose

A Fitbit tracker or scale quietly builds up years of weight, activity, heart rate, and sleep data — but it all sits behind Google's own app, disconnected from everything else. This project connects that data to an LLM directly, so you can ask about it in plain language instead of scrolling through graphs, and — the reason this exists in the first place — compare it against other trackers like [FatSecret](https://github.com/mregen/fatsecret-mcp) instead of re-entering the same numbers by hand.

A couple of examples of what that looks like once the tools are connected (illustrative - your own numbers will differ):

> **You:** How did I sleep this week compared to last?
>
> **Claude:** This week you averaged 6h 42m asleep per night across 6 nights logged, up from 6h 10m the week before. Wednesday was your most restless night (58 minutes awake); Sunday was your best, at 7h 15m asleep with only 12 minutes awake.

> **You:** Is my Fitbit weight actually making it into FatSecret?
>
> **Claude:** Checking both... Google Health has 12 weigh-ins this month from your scale, but only 3 are logged in FatSecret. Want me to add the other 9? One thing to know: FatSecret only accepts entries from the last 2 days, so this only works for recent gaps — anything older would need to be entered by hand in FatSecret's own app.

That second example is a real constraint this project ran into, not a hypothetical — see [Status](#status) below.

## Requirements

- **.NET 8 or .NET 10 runtime** - to install and run the tool (see [Install](#install) below)
- **An MCP-capable LLM client** - Claude Code, Claude Desktop, or LM Studio (see [Configure Claude Code, Claude Desktop, or LM Studio](#configure-claude-code-claude-desktop-or-lm-studio) below)
- **A Google account and a Google Cloud project** - with the Google Health API enabled and an OAuth 2.0 client of your own; see [`docs/google-cloud-setup.md`](docs/google-cloud-setup.md) for the full walkthrough

## Status

Working prototype, verified against a real account.

| Feature | Status |
|---|---|
| Weight history | ✅ Working, verified live |
| Activity (steps, calories, active minutes) | ✅ Working, verified live |
| Heart rate | ✅ Working, verified live |
| Sleep | ✅ Working, verified live - naps and main sleep on the same night aren't distinguished yet ([issue #17](https://github.com/mregen/fitbit-mcp/issues/17)) |
| Body fat percentage | ✅ Working, verified live |
| Syncing weight into FatSecret | ⚠️ Works only for recent gaps (last ~2 days) - FatSecret's own API refuses to backdate further, discovered live, not something this project can work around |
| Legacy Fitbit Web API (as an alternative to Google Health) | ⏳ Not built - Google Health already covers the same data |
| Hosting for more than one person | ⏳ Design sketch only - see [`docs/cloud-deployment.md`](docs/cloud-deployment.md) |

See [open issues](https://github.com/mregen/fitbit-mcp/issues) for the current roadmap.

## Security

The HTTP transport has **no authentication or authorization layer yet** - anyone who can reach
the endpoint can call any tool, including the ones reading your real Google Health data. This is
fine for local use (stdio, or `--http` left on `localhost`), but it means **this must not be
exposed publicly** - no public Docker hosting, no binding to `0.0.0.0` on an open network - until
that gap is closed. See [`docs/cloud-deployment.md`](docs/cloud-deployment.md) for the auth work
that's needed first and the reasoning behind deferring hosted/multi-user use.

## Available tools

| Tool | Auth needed | Notes |
|---|---|---|
| `get_weight_history` | `health_metrics_and_measurements.readonly` | Body-weight entries for a month |
| `get_activity_summary` | `activity_and_fitness.readonly` | Daily steps, calories, and active minutes for a date range (last 14 days by default) |
| `get_heart_rate_history` | `activity_and_fitness.readonly` | Daily average/min/max heart rate for a date range (last 14 days by default) |
| `get_sleep_history` | `sleep.readonly` | Sleep sessions for a month - time asleep, awake, and in bed |
| `get_body_fat_history` | `health_metrics_and_measurements.readonly` | Body-fat percentage entries for a month (same scope as weight) |

## Install

Requires the .NET 8 or .NET 10 SDK - the package multi-targets both, so `dotnet tool install`
picks whichever one matches your installed SDK automatically.

```bash
dotnet tool install --global FitbitMcp
```

This installs a `fitbit-mcp` command. Confirm it's on your PATH with `fitbit-mcp --version`
(the .NET tools directory, `~/.dotnet/tools`, needs to be there - the installer usually adds it
automatically).

You'll also need a Google Cloud project with the Google Health API enabled and an OAuth 2.0
client of your own - see [`docs/google-cloud-setup.md`](docs/google-cloud-setup.md) for the full,
step-by-step walkthrough (it covers the OAuth consent screen, scopes, and client type in detail,
which is easy to get wrong on a first pass).

## Configure credentials

Credentials are passed as environment variables - `GoogleHealth:ClientId` etc. become
`GoogleHealth__ClientId` (double underscore in place of `:`), which is how .NET's config system
maps env vars automatically.

| Setting | Environment variable |
|---|---|
| OAuth 2.0 client id | `GoogleHealth__ClientId` |
| OAuth 2.0 client secret | `GoogleHealth__ClientSecret` |

That's the only credential input needed - unlike some MCP servers, you don't manage access or
refresh tokens by hand. The one-time authorization below captures and stores them for you, and
they refresh automatically after that.

### One-time authorization

This grants the server access to your own Google Health data. It's interactive - a browser opens
for you to sign in and approve access - and finishes automatically, no code to copy by hand:

```bash
export GoogleHealth__ClientId="<your client id>"
export GoogleHealth__ClientSecret="<your client secret>"
fitbit-mcp auth login
```

You only need to do this again if access is revoked, or roughly every 7 days if your Google Cloud
project's OAuth consent screen is still in "Testing" status - see
[`docs/google-cloud-setup.md`](docs/google-cloud-setup.md#token-lifetimes-and-why-testing-status-matters)
for why, and what it means in practice.

## Run it

The server supports two transports, chosen at startup - **stdio by default**, or HTTP via a flag:

```bash
fitbit-mcp                                       # stdio - for MCP clients that spawn the process directly
fitbit-mcp --http --urls http://localhost:5230    # HTTP - a long-running server on a port
```

In HTTP mode the MCP endpoint is at `<url>/mcp` (Streamable HTTP), e.g. `http://localhost:5230/mcp`.

## Configure Claude Code, Claude Desktop, or LM Studio

Do the credentials + one-time authorization steps above first - that part is always interactive
and can't happen from inside a client's spawned process.

**Can these clients start `fitbit-mcp` automatically?** Yes - all three spawn a local stdio
process directly from their own config, no server to keep running yourself.

### Find the installed binary's absolute path

GUI-launched apps often don't inherit the PATH your terminal has, so the bare `fitbit-mcp`
command name may not resolve even though it works in a shell. Use the absolute path instead:

- macOS/Linux: `~/.dotnet/tools/fitbit-mcp`
- Windows: `%USERPROFILE%\.dotnet\tools\fitbit-mcp.exe`

### Claude Code

```bash
claude mcp add fitbit-mcp -- ~/.dotnet/tools/fitbit-mcp
```

Restart or reconnect your Claude Code session - new MCP registrations aren't picked up
mid-session.

### Claude Desktop

Edit `claude_desktop_config.json` (macOS:
`~/Library/Application Support/Claude/claude_desktop_config.json`; Windows:
`%APPDATA%\Claude\claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "fitbit-mcp": {
      "command": "/Users/you/.dotnet/tools/fitbit-mcp",
      "args": [],
      "env": {
        "GoogleHealth__ClientId": "<your client id>",
        "GoogleHealth__ClientSecret": "<your client secret>"
      }
    }
  }
}
```

Restart Claude Desktop to pick it up.

### LM Studio

LM Studio's MCP config (`mcp.json`) follows the same `command`/`args`/`env` shape as Claude
Desktop above. Its documented path is `~/.lmstudio/mcp.json` (macOS/Linux) /
`%USERPROFILE%\.lmstudio\mcp.json` (Windows), but there are user reports of the real path
differing by version/OS - rather than guessing, use the in-app editor: **Program tab → Install
→ Edit `mcp.json`**, which opens whichever file is actually authoritative for your install, and
paste in the same JSON shown for Claude Desktop above (just the inner object works too, since
LM Studio also uses an `mcpServers` map).

## Building from source / contributing

Not needed just to use the tool - see [`docs/DEVELOPER.md`](docs/DEVELOPER.md) in the repo for
running from a clone, architecture notes, and how NuGet publishing works.

## License

MIT — see [LICENSE](LICENSE).
