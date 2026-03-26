# Pattern 3 — Azure Bot Service (Direct Line / Custom Website)

## Architecture

```
User (Browser) → Web Chat → Direct Line Channel → Bot (ASP.NET Core)
                                                       │
                                              OBO token exchange
                                                       │
                                                       ▼
                                               Enterprise API
```

## Overview

This pattern demonstrates a **Bot Framework v4** bot exposed through the **Direct Line** channel and consumed by a **custom website** using the **Bot Framework Web Chat** control.

Two authentication options are demonstrated side-by-side:

| Option | Flow | When to use |
|--------|------|-------------|
| **A — Backchannel** | Web client signs the user in with MSAL.js, then sends the access token to the bot via a Direct Line `event` activity. | You own the web client and can embed auth logic. |
| **B — OAuth Prompt** | The bot uses the built-in `OAuthPrompt` dialog to trigger a sign-in card inside Web Chat. | Fallback when no backchannel token is available. |

In both cases the bot performs an **On-Behalf-Of (OBO)** token exchange to call a downstream **Enterprise API**.

## Components

| Component | Path | Port |
|-----------|------|------|
| **DirectLineBot** | `src/DirectLineBot/` | `http://localhost:5040` |
| **WebClient** | `src/WebClient/` | Open `index.html` directly |
| **Enterprise API** (shared) | `../shared/EnterpriseApi/` | `http://localhost:5050` |

## Prerequisites

- .NET 8 SDK
- An Azure Bot resource with the **Direct Line** channel enabled
- Entra ID (Azure AD) app registrations:
  - **Bot app** — `MicrosoftAppId` / `MicrosoftAppPassword`, with an OAuth connection named `EntraIdConnection`
  - **Web client app** — public client for MSAL.js (SPA redirect URI)
  - **Enterprise API app** — exposes the `access_as_user` scope

## How to Run

1. **Start the Enterprise API** (Pattern 1 / shared):

   ```bash
   cd ../shared/EnterpriseApi
   dotnet run
   ```

2. **Start the DirectLineBot**:

   ```bash
   cd src/DirectLineBot
   dotnet run --urls http://localhost:5040
   ```

3. **Open the WebClient**:

   Open `src/WebClient/index.html` in a browser (or serve via a local HTTP server).

4. **Try both auth options**:
   - **Option A** — Check "Send token via backchannel", click **Sign In with Microsoft**, then chat.
   - **Option B** — Leave the toggle unchecked. The bot will present an OAuth sign-in card.

## Configuration

### DirectLineBot — `appsettings.json`

| Key | Description |
|-----|-------------|
| `MicrosoftAppId` | Bot's Entra ID application (client) ID |
| `MicrosoftAppPassword` | Bot's client secret |
| `MicrosoftAppTenantId` | Your Entra ID tenant ID |
| `ConnectionName` | OAuth connection name configured on the Bot resource |
| `DirectLineSecret` | Direct Line channel secret (from Azure Portal → Bot → Channels) |
| `EnterpriseApi:BaseUrl` | Enterprise API base URL |
| `EnterpriseApi:Scope` | Scope to request during OBO |

### WebClient — `app.js`

Update the placeholders at the top of the file:

| Placeholder | Description |
|-------------|-------------|
| `<WEBCLIENT_APP_ID>` | SPA app registration client ID |
| `<TENANT_ID>` | Entra ID tenant ID |
| `<BOT_APP_ID>` | Bot app ID (used as scope audience) |

## What This Proves

1. **Direct Line token exchange** — The bot backend exchanges a Direct Line *secret* for a short-lived *token*, keeping the secret off the client.
2. **Web Chat integration** — The Bot Framework Web Chat control connects via Direct Line with the exchanged token.
3. **Backchannel auth (Option A)** — A pre-authenticated user token is delivered to the bot through a Direct Line `event` activity, eliminating the need for a separate sign-in prompt.
4. **OAuth Prompt fallback (Option B)** — When no backchannel token is available, the standard `OAuthPrompt` dialog drives the sign-in flow.
5. **OBO → Enterprise API** — Regardless of how the user token was obtained, the bot performs an OBO exchange and calls the downstream Enterprise API on behalf of the user.
