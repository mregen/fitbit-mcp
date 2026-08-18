# CLAUDE.md

This file provides guidance to Claude Code when working in this repository.

## Project

A .NET-based Model Context Protocol (MCP) server for the [Google Health API](https://developers.google.com/health) (Fitbit and Pixel Watch data). Built as a sibling project to [fatsecret-mcp](../fatsecret-mcp), following the same conventions (solution layout, license, tooling, NUnit, MCP tool patterns).

The driving use case: the developer's Fitbit Aria 2 scale syncs weight into Google Health, but that weight never makes it into FatSecret (activity already syncs Fitbit → FatSecret; weight doesn't). fatsecret-mcp already has `get_weight_history`/`add_weight_entry` tools. This project's job is to expose the Google Health side — a read-only weight tool — so a Claude session with both MCP servers connected can compare the two and fill in what's missing, with neither server needing the other's credentials.

## Status as of 2026-08-18

- Pushed to GitHub: [mregen/fitbit-mcp](https://github.com/mregen/fitbit-mcp), public, branch `main`. MIT licensed (`LICENSE` + SPDX headers per source file).
- 10 issues filed across 3 milestones, all open. **Milestone 1** (Prototype: weight sync working end to end) — #1, #2, #3. **Milestone 2** (Legacy Fitbit Web API phase) — #4, #5, #6. **Milestone 3** (Harden & containerize) — #7, #8, #9, #10.
- **Not yet verified against a live account.** `dotnet build`/`dotnet test` pass (7 NUnit tests) and the server smoke-tests fine (`initialize` handshake over `/mcp` succeeds locally), but nobody has completed the Google OAuth flow or called the real Google Health API yet — see #1 and #2.
- **Tool coverage** (`src/FitbitMcp/Tools/`): just `WeightTools.GetWeightHistory` (`get_weight_history`). Deliberately scoped down to only what the first use case needs — no Activity/Sleep/HeartRate/Nutrition tools yet, no legacy Fitbit Web API integration yet (that's Milestone 2, issues #4/#5).
- **Auth** (`src/FitbitMcp/Auth/`): OAuth 2.0 authorization-code + PKCE against Google's endpoints (`GoogleHealthOAuth2Client`), a loopback-listener CLI bootstrap (`AuthCli`, `dotnet run -- auth login`), and a `TokenStore` that reads/writes this project's `dotnet user-secrets` backing file directly (`GoogleHealth:AccessToken`/`RefreshToken`/`ExpiresAtUtc`) rather than introducing a separate dotfile convention. `GoogleHealthApi` refreshes automatically on 401 or stored expiry.
- **Known unverified assumption, flagged in code comments (`GoogleHealthApi.cs`, `WeightTools.cs`) and issue #2**: the `dataPoints:rollUp` request body and the bucket/dataset/point/value response shape were written from Google's REST reference page, not a confirmed live response. Very likely needs adjusting once #1 unblocks real testing.
- **No token persistence design for anything beyond a single local dev machine yet** — same open question fatsecret-mcp has (its own #11); tracked here as #7.
- **Tests**: `tests/FitbitMcp.Tests` (NUnit), 7 tests covering `GoogleHealthOAuth2Client` (authorize-URL building, code exchange, refresh, error handling — via a stub `HttpMessageHandler`, no network) and `WeightTools.ParseEntries` (bucket/dataset/point/value → flat entries, against hand-written sample JSON, not a real response). `.github/workflows/build.yml` runs restore/build/test on push and PR to `main`.
- Next action is on the developer: register a Google Cloud OAuth client and run `auth login` against the real account (#1), which unblocks #2 and #3.

## Architecture plan

- **MCP transport**: official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.AspNetCore`), `.WithHttpTransport()` + `MapMcp()`, same as fatsecret-mcp.
- **Auth**: OAuth 2.0 authorization-code + PKCE (Google's standard flow), simpler than fatsecret-mcp's OAuth1 three-legged flow but still config-driven with nothing committed to the repo.
- **Sync mechanism is deliberately not code**: no bespoke sync tool/pipeline lives in this repo or fatsecret-mcp. The "does it match / fill in gaps" work happens live, in a Claude session with both MCP servers connected, by calling each server's own tools. This keeps the two servers independent (neither holds the other's credentials) and avoids building automation for what is, so far, a one-off personal reconciliation task.
- **Target framework**: `net10.0`, matching fatsecret-mcp.
- **Deferred**: legacy Fitbit Web API (activity/sleep/heart rate/nutrition) as a second data path (Milestone 2). No abstraction layer (e.g. `IHealthDataProvider`) has been introduced yet — deliberately not built speculatively ahead of a second real implementation to abstract over (see #6).

## Next steps (pick up here)

See GitHub Issues on [mregen/fitbit-mcp](https://github.com/mregen/fitbit-mcp/issues) for the full, current task list. Open issues:

- **#1** — register a Google Cloud OAuth client and complete `auth login` against a real account. Developer action; blocks #2 and #3.
- **#2** — verify the `dataPoints:rollUp` request/response shape against the live API once #1 unblocks it; fix `GoogleHealthApi`/`WeightTools` if the real shape differs from the current best-effort guess.
- **#3** — the actual acceptance test: an end-to-end weight-sync session using both fitbit-mcp and fatsecret-mcp in a live Claude session.
- **#4–#6** — legacy Fitbit Web API phase, not started.
- **#7–#10** — hardening (token persistence design, Dockerfile, more tests, more docs), not started.
