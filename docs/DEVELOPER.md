# Developer guide

Notes for building, publishing, and understanding the internals of `fitbit-mcp`. If you just
want to run the tool, see the main [README](../README.md) instead - nothing here is needed for
that.

## Architecture

- Built on the official [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (`ModelContextProtocol.AspNetCore`). Two transports, chosen at startup by branching on `args` before the host is built: stdio (default, `Host.CreateApplicationBuilder` + `.WithStdioServerTransport()`) or HTTP (`--http`, `WebApplication.CreateBuilder` + stateless Streamable HTTP via `MapMcp`) - the SDK doesn't support registering both on one builder, so `Program.cs` picks one.
- Also packable as a .NET global tool (`PackAsTool`). Docker/multi-user hosting is intentionally not built yet - see [`cloud-deployment.md`](cloud-deployment.md).
- Auth: OAuth 2.0 authorization-code + PKCE against Google's endpoints (`src/FitbitMcp/Auth/`), requesting a single space-separated multi-scope string (`health_metrics_and_measurements.readonly`, `activity_and_fitness.readonly`, `sleep.readonly`). See [`google-cloud-setup.md`](google-cloud-setup.md) for the Google Cloud Console walkthrough and, importantly, why refresh tokens need renewing weekly while the OAuth consent screen stays in "Testing" status.
- Token storage design worth calling out: `TokenStore` writes access/refresh tokens directly to the same file `dotnet user-secrets` manages (keyed by this project's fixed `UserSecretsId`), rather than a separate dotfile convention. Unlike relying on the `dotnet user-secrets` *CLI* (which needs a project directory), this is plain file I/O keyed off a compile-time-embedded assembly attribute - it works identically whether run from source or as a globally-installed tool, no project directory needed either way. Client id/secret, by contrast, are read through ordinary `IConfiguration`, so they can come from either user-secrets (source) or environment variables (installed tool) - see the README's [Configure credentials](../README.md#configure-credentials) section.
- Target: multi-targets `net8.0;net10.0` (net8.0 is the still-widely-installed LTS through ~Nov 2026; net10.0 is the current LTS through ~Nov 2028) - single project for now, no RID-specific builds.

See also [`cloud-deployment.md`](cloud-deployment.md) for the (not yet started) sketch of what
running this as an invite-only service for multiple people would take.

## Running from source

The [README](../README.md) covers installing the published tool from nuget.org - this is for
working against a clone of this repo instead (e.g. to test unreleased changes).

### Prerequisites

- .NET 8 or .NET 10 SDK (the project multi-targets both - either builds and runs it)
- A Google Cloud project with the Google Health API enabled and an OAuth 2.0 client - see [`google-cloud-setup.md`](google-cloud-setup.md) for the full walkthrough

### 1. Configure credentials

Credentials are read from [.NET user-secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets), run from `src/FitbitMcp/` - never commit them to the repo.

```bash
cd src/FitbitMcp
dotnet user-secrets set GoogleHealth:ClientId "<client id>"
dotnet user-secrets set GoogleHealth:ClientSecret "<client secret>"
```

### 2. Complete the OAuth 2.0 flow (one time)

This grants the server access to your own Google Health data. It's interactive - a browser opens for consent - and completes automatically via a short-lived local callback listener:

```bash
cd src/FitbitMcp
dotnet run -- auth login
```

Tokens are cached in the same `dotnet user-secrets` store and refreshed automatically after that - see [`google-cloud-setup.md`](google-cloud-setup.md#token-lifetimes-and-why-testing-status-matters) for when you'd need to repeat this.

### 3. Run the server

The server supports two transports, chosen at startup - **stdio by default**, or HTTP via a flag:

```bash
cd src/FitbitMcp

dotnet run --no-launch-profile                                          # stdio - for MCP clients that spawn the process directly
dotnet run --no-launch-profile -- --http --urls http://localhost:5230   # HTTP - a long-running server on a port
```

In HTTP mode the MCP endpoint is at `<url>/mcp` (Streamable HTTP), e.g. `http://localhost:5230/mcp`.

**Never point a real MCP client at plain `dotnet run` for stdio** - its own "Building..." banner
pollutes stdout before the app starts, which corrupts the JSON-RPC channel (confirmed live). Use
`--no-launch-profile` as shown above, the built DLL, or an installed tool instead.

### 4. Point an MCP client at it

A project-scoped [`.mcp.json`](../.mcp.json) is checked into this repo - it needs no secrets
embedded (credentials come from `dotnet user-secrets`, not env vars, for the source-dev path), so
it's safe to commit. Open the project in Claude Code and
it'll prompt once to trust the server.

To register manually instead:

```bash
claude mcp add fitbit-mcp -- dotnet run --project src/FitbitMcp --no-launch-profile
```

Or HTTP, with the server already running from step 3:

```bash
claude mcp add --transport http fitbit-local-dev http://localhost:5230/mcp
```

Then restart or reconnect your Claude Code session - new MCP registrations aren't picked up
mid-session.

### Packing and installing a local build as a tool

To test the tool as it would actually be installed, without waiting on a NuGet.org publish:

```bash
dotnet pack src/FitbitMcp/FitbitMcp.csproj -c Release -o ./nupkg
dotnet tool install --global --add-source ./nupkg FitbitMcp
```

This installs the same `fitbit-mcp` command described in the README, from your local build.

## Publishing to NuGet.org

`.github/workflows/build.yml` packs the tool (`.nupkg` + `.snupkg`) on every push to `main` and
uploads it as a workflow artifact - so a build is always inspectable. Publishing to nuget.org is
a **separate, manual-only job** that never runs on a normal push/PR: it only exists on
`workflow_dispatch` (Actions tab → this workflow → **Run workflow**), gated behind a `publish`
checkbox input that **defaults to false**, and it `needs: build` so it can't run unless the
build+test job already succeeded.

Publishing uses nuget.org's [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
(OIDC) instead of a stored API key - no long-lived secret in this repo at all. One-time setup,
on nuget.org (**username menu → Trusted Publishing → Add policy**):

| Field | Value |
|---|---|
| Repository Owner | `mregen` |
| Repository | `fitbit-mcp` |
| Workflow File | `build.yml` |
| Environment | `nuget-publish` |

The `publish` job passes `${{ github.repository_owner }}` as `NuGet/login`'s `user:` input,
rather than a hardcoded name - it relies on your nuget.org profile name matching your GitHub
username, so a fork of this repo publishes under *its own* owner's identity by default, without
editing the workflow. If your nuget.org username ever differs from your GitHub username, override
`user:` with a literal value (or a repository variable) instead.

After a successful push, the same job also creates a GitHub release (`gh release create`,
tagged `v<version>`) with the `.nupkg`/`.snupkg` attached and notes auto-generated from merged
PRs/commits since the last tag. The version is the one NBGV computed during `Pack`, extracted
from the packed filename (`FitbitMcp.<version>.nupkg`) and passed between jobs via a job
`output`.

This isn't primarily a security gate (the package contains no
credentials either way) so much as a "not polished enough yet" one - keep publishing manual-only
until the tool has had more real-world exercise beyond this project's own testing.
