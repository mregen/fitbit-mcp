# CLAUDE.md

This file provides guidance to Claude Code when working in this repository.

## Project

A .NET-based Model Context Protocol (MCP) server for the [Google Health API](https://developers.google.com/health) (Fitbit and Pixel Watch data). Built as a sibling project to [fatsecret-mcp](../fatsecret-mcp), following the same conventions (solution layout, license, tooling, NUnit, MCP tool patterns).

The driving use case: the developer's Fitbit Aria 2 scale syncs weight into Google Health, but that weight never makes it into FatSecret (activity already syncs Fitbit → FatSecret; weight doesn't). fatsecret-mcp already has `get_weight_history`/`add_weight_entry` tools. This project's job is to expose the Google Health side — a read-only weight tool — so a Claude session with both MCP servers connected can compare the two and fill in what's missing, with neither server needing the other's credentials.

## Status as of 2026-08-18 (end of first working session)

**The prototype is done and the core question has been answered — with an important caveat found along the way.**

- Pushed to GitHub: [mregen/fitbit-mcp](https://github.com/mregen/fitbit-mcp), public, branch `main`. MIT licensed (`LICENSE` + SPDX headers per source file).
- **Auth and `get_weight_history` both work against the real account.** Getting there took two rounds of fixing wrong guesses:
  1. First `auth login` attempt failed with Google's `invalid_scope` error — the scope this code requested (`googleapis.com/auth/health.rollup`) doesn't exist; it was a guess from a REST reference *page* rather than the actual API contract.
  2. Pulled the live discovery document (`https://health.googleapis.com/$discovery/rest?version=v4`) to get the real scope (`googleapis.com/auth/googlehealth.health_metrics_and_measurements.readonly`) and, while there, found the `dataPoints:rollUp` request/response shape was also wrong (modeled on the legacy Google Fit v1 aggregate API — nested `bucket`/`dataset`/`point`/`value` — instead of v4's actual flat `{ rollupDataPoints: [{ startTime, weight: { weightGramsAvg } }] }`, in grams not kg).
  3. After both fixes, `auth login` succeeded and a live call returned real weight data (11 days of August readings, 99–102 kg range) correctly parsed and converted to kg.
- **Ran the actual end-to-end comparison against fatsecret-mcp for August**: 8 of 11 Google Health days were missing from FatSecret; the 3 present were off by 0.1–0.35 kg (Fitbit's native device sync uses a single reading, Google Health's rollup is a full-day average — a methodology difference, not an error on either side).
- **Known limitation, discovered live, not a bug in this codebase**: attempted to backfill all 11 missing/mismatched days via fatsecret-mcp's `add_weight_entry`. Every call failed with FatSecret error 205 — *"Date must be within 2 days from today"* — undocumented on FatSecret's own `weight.update` reference page. **This API cannot backfill historical weight data at all**, no matter which MCP server drives it. The sync approach only works for weigh-ins within the last ~2 days; anything older needs manual entry through FatSecret's own app. Full write-up on issue #3 (closed) and a follow-up filed as #11 (never actually confirmed a *successful* write, since nothing in scope was in-window that day).
- **Tool coverage** (`src/FitbitMcp/Tools/`): just `WeightTools.GetWeightHistory` (`get_weight_history`), deliberately scoped to only the first use case — no Activity/Sleep/HeartRate/Nutrition tools, no legacy Fitbit Web API integration (that's Milestone 2, issues #4–#6).
- **Auth implementation** (`src/FitbitMcp/Auth/`): OAuth 2.0 authorization-code + PKCE (`GoogleHealthOAuth2Client`), a loopback-listener CLI bootstrap (`AuthCli`, `dotnet run -- auth login`), and a `TokenStore` that reads/writes this project's `dotnet user-secrets` backing file directly rather than a separate dotfile convention. `GoogleHealthApi` refreshes automatically on 401 or stored expiry. The Google Cloud OAuth client must be **Web application** type (not Desktop) with `http://127.0.0.1:3000/callback` registered exactly under Authorized redirect URIs — this code uses a fixed port+path, not Google's dynamic-loopback-port exception for Desktop-type clients.
- **Infrastructure parity with fatsecret-mcp** (separate earlier pass this session): dual stdio/HTTP transport (stdio default, `--http` flag), `dotnet tool` packaging (`fitbit-mcp` command), Nerdbank.GitVersioning for automatic semantic versioning, an explicit `AddUserSecrets<Program>()` fix so secrets still load outside `ASPNETCORE_ENVIRONMENT=Development`.
- **Tests**: `tests/FitbitMcp.Tests` (NUnit), 7 tests covering `GoogleHealthOAuth2Client` (authorize-URL building, code exchange, refresh, error handling — via a stub `HttpMessageHandler`, no network) and `WeightTools.ParseEntries` (now matching the confirmed-live `rollupDataPoints` shape). `.github/workflows/build.yml` runs restore/build/test on push and PR to `main`.

## Architecture plan

- **MCP transport**: official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.AspNetCore`). Dual transport, chosen at startup by branching on `args`: stdio (default, `Host.CreateApplicationBuilder` + `WithStdioServerTransport`) or HTTP (`--http`, `WebApplication.CreateBuilder` + stateless Streamable HTTP via `MapMcp`) — same pattern as fatsecret-mcp, since the SDK can't register both transports on one builder.
- **Auth**: OAuth 2.0 authorization-code + PKCE (Google's standard flow), simpler than fatsecret-mcp's OAuth1 three-legged flow but still config-driven with nothing committed to the repo.
- **Sync mechanism is deliberately not code**: no bespoke sync tool/pipeline lives in this repo or fatsecret-mcp. The "does it match / fill in gaps" work happens live, in a Claude session with both MCP servers connected, by calling each server's own tools. This keeps the two servers independent (neither holds the other's credentials) and avoids building automation for what turned out to have a hard platform ceiling (FatSecret's 2-day backdate limit) anyway — no amount of in-repo automation would work around that.
- **Target framework**: `net10.0`, matching fatsecret-mcp. Also packable as a `dotnet tool` (`PackAsTool`), matching fatsecret-mcp.
- **Deferred**: legacy Fitbit Web API (activity/sleep/heart rate/nutrition) as a second data path (Milestone 2). No abstraction layer (e.g. `IHealthDataProvider`) has been introduced yet — deliberately not built speculatively ahead of a second real implementation to abstract over (see #6).
- **Cloud multi-tenant deployment**: design sketch only, not implemented — see [docs/cloud-deployment.md](docs/cloud-deployment.md). Covers running an invite-only beta (Google's OAuth "Testing" mode allowlist doubles as the invite mechanism), per-user token storage, and MCP-level bearer auth, while leaving this single-user dev mode untouched. Tracked as the "Cloud multi-tenant service" milestone.

## Status update, 2026-08-19

- **Issue #11 closed**: fetched today's real weight from Google Health (101.4 kg), confirmed it was missing from FatSecret, wrote it via `add_weight_entry`, and re-verified it landed correctly. This is the first confirmed *successful* write — the ongoing (not historical) sync case is now proven end to end. All of Milestone 1 (#1, #2, #3, #11) is closed.
- Sketched a cloud multi-tenant deployment design (see above) after being asked whether this could be hosted for multiple invited users. Also answered, inline in conversation (not yet in a design doc), whether more Google Health data types (activity/heart-rate, sleep, body-fat, etc.) or write access are reachable — yes to both, via additional scopes, but explicitly deferred as separate future work.

## Next steps (pick up here)

See GitHub Issues on [mregen/fitbit-mcp](https://github.com/mregen/fitbit-mcp/issues) for the full, current task list.

**Milestone 1 (Prototype) fully closed**: #1 (auth works), #2 (weight data confirmed live), #3 (end-to-end session run, found the 2-day backdate wall), #11 (confirmed a real write succeeds within FatSecret's window).

**Open**:
- **Milestone "Cloud multi-tenant service"** (new) — 4 issues tracking the phased roadmap in `docs/cloud-deployment.md`: token-store abstraction, MCP bearer-auth + OAuth routes, Dockerfile + real deploy (beta goes live here), Google app verification (deferred until the beta shows interest).
- **#4–#6** — legacy Fitbit Web API phase (activity/sleep/heart rate/nutrition), not started.
- **#7–#10** — hardening (token persistence design beyond a single dev machine, Dockerfile, more tests, more docs), not started. Note #8 (Dockerfile) and #7 (token persistence) substantially overlap with the new cloud-deployment milestone — worth reconciling/closing as duplicates once that work actually starts, rather than tracking the same thing twice.

**Not tracked as a repo issue, developer action**: manually backfill the historical weight gap (older than 2 days) through FatSecret's own app/website — that path presumably isn't subject to the API-only restriction found in #3.
