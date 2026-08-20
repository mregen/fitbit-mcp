# Setting up a Google Cloud OAuth client for fitbit-mcp

This project reads Google Health data using OAuth 2.0 - it needs its own Google Cloud project and OAuth client, separate from your Google account itself. This is a one-time setup, done in [Google Cloud Console](https://console.cloud.google.com).

## 1. Create or select a project

Any Google Cloud project works - a dedicated one keeps things tidy, but it's not required.

## 2. Enable the Google Health API

**APIs & Services → Library** → search for **Google Health API** → **Enable**.

## 3. Configure the OAuth consent screen

**APIs & Services → OAuth consent screen**, if not already configured:

- **User type**: External.
- **Publishing status**: leave it at **Testing** for personal use or a small invite-only group - see [Token lifetimes](#token-lifetimes-and-why-testing-status-matters) below for what this trades off.
- **Data access → Add or remove scopes**: search for and add all four scopes this project uses:
  - `.../auth/googlehealth.health_metrics_and_measurements.readonly`
  - `.../auth/googlehealth.activity_and_fitness.readonly`
  - `.../auth/googlehealth.sleep.readonly`
  - `.../auth/googlehealth.location.readonly` (only needed for `get_exercise_gps_route` - Google's own description is "See exercise GPS location data in Google Health", scoped to exercise routes rather than general location history)

  These are sensitive scopes - Google requires them explicitly added here before any OAuth client under this project can request them, even in Testing mode.
- **Test users**: add your own Google account (and, later, anyone you invite - see [`docs/cloud-deployment.md`](cloud-deployment.md)). While in Testing status, **only listed test users can complete authorization at all** - capped at 100.

## 4. Create the OAuth client

**APIs & Services → Credentials → Create Credentials → OAuth client ID**:

- **Application type**: **Web application** - not Desktop. This project uses a fixed redirect URI with a specific port and path (`http://127.0.0.1:3000/callback`); Desktop-type clients rely on Google's dynamic-loopback-port exception instead, which doesn't match a fixed path.
- **Authorized redirect URIs**: add exactly `http://127.0.0.1:3000/callback`.
- Save, and note the **Client ID** and **Client Secret** it generates.

## 5. Provide the credentials to fitbit-mcp

**Running from source** (see the main [README](../README.md) or [DEVELOPER.md](DEVELOPER.md)):

```bash
cd src/FitbitMcp
dotnet user-secrets set GoogleHealth:ClientId "<client id>"
dotnet user-secrets set GoogleHealth:ClientSecret "<client secret>"
```

**Running as an installed tool** - set as environment variables instead (double underscore in place of `:`, same convention .NET's config system uses everywhere):

```bash
export GoogleHealth__ClientId="<client id>"
export GoogleHealth__ClientSecret="<client secret>"
```

## 6. Complete the one-time authorization

```bash
fitbit-mcp auth login          # installed tool
dotnet run -- auth login       # from source
```

This opens a browser for you to sign in and approve access, and finishes automatically via a short-lived local callback listener - no code to copy by hand. The resulting tokens are then stored and refreshed automatically; you don't need to repeat this unless access is revoked, or the 7-day Testing-status limit below is hit.

## Token lifetimes (and why "Testing" status matters)

Two different tokens are involved, with very different lifetimes:

- **Access token**: the one actually used to call the Google Health API. Expires in about an hour. **You don't need to do anything about this** - fitbit-mcp refreshes it automatically using the refresh token below, before every call if needed.
- **Refresh token**: the long-lived credential used to silently obtain new access tokens. Normally these last until you revoke access. **However**, while the OAuth consent screen's publishing status is **Testing** (the default, and what step 3 above sets up), Google expires refresh tokens **7 days after authorization**, regardless of use - this is a deliberate Google policy for unverified apps, not a bug or a setting in this project.

**What this means in practice**: if you leave the app in Testing status, tool calls will start failing with an authentication error about a week after you last ran `auth login` - the fix is just to run it again. This is the realistic mode of operation for personal use and the invite-only beta sketched in [`docs/cloud-deployment.md`](cloud-deployment.md).

Moving the consent screen to **In production** status removes the 7-day limit, but shows Google's "unverified app" warning to every user (not just the test users you've listed), and may carry its own prerequisites for sensitive scopes like the ones here - not attempted or verified as part of this project. Worth revisiting only if this moves beyond a small invite-only group.
