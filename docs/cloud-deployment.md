# Cloud deployment: an invite-only multi-tenant fitbit-mcp

## Status

Design sketch, not implemented. Written up 2026-08-19 in response to the question "could this run as a hosted service for multiple people, each seeing only their own data?" Nothing in this document changes the existing single-user dev mode (`dotnet tool` + `dotnet user-secrets`, described in the main [README](../README.md)) — that keeps working exactly as it does today, unmodified.

## Goal

Run an **invite-only beta**: a handful of real people, each explicitly allowlisted, using their own Google Health data through one hosted MCP endpoint — to see whether this gets any real interest before investing further (broader launch, formal Google verification, etc.). Not designed for public/unbounded scale.

## Why this is architecturally sound in the first place

A Google OAuth client id/secret identifies the *application* you registered in Google Cloud, not a person. Any number of different Google accounts can each complete their own consent flow against that one registered client and receive their own, independently-scoped tokens. Google's API then only ever returns data for whichever token made the request — cross-user isolation is a property of the OAuth model itself, not something this project has to build.

What's actually missing today is entirely on this project's side:

1. A hosted (non-`localhost`) place for people to complete that consent flow.
2. Somewhere to store *many* people's tokens instead of one local file.
3. A way for the MCP server to know, for each incoming request, whose stored tokens to use.

## What exists today vs. what's needed

| Today (single-user dev mode) | Needed for cloud multi-tenant |
|---|---|
| `AuthCli` opens a browser and runs a temporary `HttpListener` on `http://127.0.0.1:3000/callback` | A permanent, hosted HTTPS route, e.g. `/oauth/google/callback`, plus a `/connect` route to start the flow (there's no equivalent today — `auth login` *is* the whole flow, run once, interactively, by the one person using the tool) |
| `TokenStore` reads/writes one `GoogleHealth:*` block in this project's `dotnet user-secrets` file | An `IHealthTokenStore` interface (`LoadAsync(userId)` / `SaveAsync(userId, tokens)`), with the existing `TokenStore` renamed/kept as `LocalUserSecretsTokenStore` (unchanged behavior) alongside a new `CloudTokenStore` implementation, keyed by Google account id, backed by SQLite on a persistent volume — plenty for an invite-only beta, no managed database needed. Tokens should be encrypted at rest (ASP.NET Core's Data Protection APIs) given the sensitivity of health-scoped OAuth tokens, more so than the single-local-user case. |
| No concept of "caller identity" — there's only ever one implicit user | Every MCP HTTP request needs to resolve to a specific person. At the end of `/connect`, mint an opaque per-user "MCP API token" (shown once, pasted into the user's MCP client config) and validate it as a Bearer token via standard ASP.NET Core authentication middleware on every `/mcp` request. That token is purely a lookup key back to "which Google account's tokens to use" — Google's own tokens never leave the server. This is the same `IHttpContextAccessor`-based caller-identity pattern the MCP C# SDK already documents for tools that need to know who's calling. |
| `Program.cs` branches on `args` into `auth` / `--http` / stdio-default | A new `--cloud` mode alongside the existing three, wiring up `CloudTokenStore` + bearer-auth middleware + the `/connect` and `/oauth/google/callback` routes via an `AddGoogleHealthClients`-style DI extension. The existing three branches are untouched. |
| Runs via `dotnet run`, packaged as a `dotnet tool` (`PackAsTool`) | The `--cloud` mode gets packaged as a Docker container instead — this is exactly what the already-open, untouched issue #8 ("Write Dockerfile and verify containerized run") already calls for. Any container host with a persistent volume works (a VPS, Fly.io, Railway, Azure Container Apps with a mounted file share) — no specific provider needs picking at sketch stage. The developer has floated Azure DevOps or another free-tier provider specifically for this invite-only testing phase — worth evaluating against the others once this phase actually starts, not decided yet. |

## Invite gating is (almost) free

Google's OAuth consent screen has a "Testing" publishing status — capped at 100 explicitly-listed test users, added one at a time by the developer in Google Cloud Console. For an invite-only beta, that cap and allowlist **is** the invite mechanism: "inviting" someone means adding their Google account email to that list. Nobody not on it can complete consent at all, so there's no risk of random signups either consuming a slot or reaching real data.

A lightweight app-level check on `/connect` (an invite code, say) is worth considering purely for friendlier UX than Google's own generic error page for someone who stumbles onto the URL uninvited — but it's not required for correctness. The real gate is Google's allowlist.

**Formal Google OAuth app verification is explicitly out of scope for this beta.** It only becomes relevant if the beta shows enough interest to justify opening access beyond 100 explicitly-invited people. Because the scope involved (`googlehealth.health_metrics_and_measurements.readonly`, and any future health scopes) is sensitive, verification would likely require more than Google's basic tier — possibly a formal security assessment, taking days to weeks, entirely outside this project's control. Until/unless that happens, every invited user will see Google's "unverified app" warning during consent — acceptable for people who already trust you enough to be personally invited, not for a general public launch.

**Confirmed real constraint, discovered live: staying in "Testing" status also means every invited person's refresh token expires 7 days after they authorize**, not just the developer's own (see [`google-cloud-setup.md`](google-cloud-setup.md#token-lifetimes-and-why-testing-status-matters) for the underlying Google policy). For the single-user dev mode, "just re-run `auth login`" is a minor inconvenience. For a hosted multi-tenant beta, this means either: (a) accepting that every invited user needs to redo the `/connect` flow roughly weekly, and building the UX around that (a clear re-auth prompt/link, not a silent failure), or (b) treating this as one more reason "In production" status is worth revisiting sooner than "only if the beta takes off" implied above. Not a blocker, but the CloudTokenStore design (phase 1 below) should account for a token that's expected to go stale on a known schedule, not just "expired, ask the user to redo OAuth" as an edge case.

## What using it actually looks like, once built

1. Developer adds the person's Google account email to the Testing-mode allowlist in Google Cloud Console (the actual "invite").
2. They visit `/connect`, sign in with Google, click through the unverified-app warning, approve consent.
3. They're shown an MCP server URL and a one-time API token to copy.
4. They add one entry to their MCP client's config: the URL plus that token as a Bearer credential.
5. Nothing further needed after that — the same one-time-setup shape as any "Connect your Google account" SaaS integration, plus the one manual allowlisting step on the developer's side per invite.

**Open question, not yet confirmed**: whether MCP clients people would actually use (e.g. Claude Code's `claude mcp add --transport http`) support attaching a custom `Authorization` header on an HTTP MCP server today. Worth checking against real client documentation before committing to the bearer-token design over an alternative (e.g. a token embedded directly in the URL path/query, which has its own tradeoffs).

## Phased roadmap

Not built yet — this is how it would be scoped into GitHub issues if/when this moves past the sketch stage:

1. **`IHealthTokenStore` + `CloudTokenStore` (SQLite)** — no auth wiring yet, locally testable in isolation.
2. **MCP bearer-auth middleware + `/connect` + `/oauth/google/callback` routes** — the actual multi-tenant auth flow, testable locally with two different Google test accounts.
3. **Dockerfile + deploy to a real host** — smoke test with a second real Google account against the live deployment. This is where the invite-only beta actually goes live.
4. **(Not started, revisit only if the beta shows real interest)** — Google OAuth app verification, to open access beyond the 100-invite Testing-mode cap.

## Non-goals for this beta

- Public, unbounded signup.
- Formal Google app verification.
- A managed database (SQLite is enough at this scale).
- Any change to the existing single-user `dotnet tool` dev mode.
