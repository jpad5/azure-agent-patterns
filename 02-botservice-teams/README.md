# Pattern 2 — Azure Bot Service (Teams Channel)

## Architecture

```
User (Teams) → Teams Client → Bot Connector → Bot (ASP.NET Core) → OBO Token Exchange → Enterprise API
```

A Bot Framework v4 bot deployed to Microsoft Teams. The bot authenticates users with an
OAuth Prompt, simulates Copilot Studio orchestration, performs an On-Behalf-Of (OBO) token
exchange, and calls a downstream Enterprise API on the user's behalf.

## Prerequisites

| Requirement | Purpose |
|---|---|
| .NET 8 SDK | Build & run the bot |
| Bot Framework Emulator | Local testing without Teams |
| Azure Bot registration | Bot identity + OAuth connection |
| Enterprise API app registration | Downstream API the bot calls via OBO |

### App Registration Setup

1. **Bot App Registration** — Register an app in Entra ID for the bot.
   - Add a client secret → set in `appsettings.json` as `MicrosoftAppPassword`.
   - Under **API permissions**, add the Enterprise API scope (`access_as_user`).
   - In the Azure Bot resource, configure an OAuth connection named **`EntraIdConnection`**
     that targets your tenant and the Enterprise API scope.

2. **Enterprise API App Registration** — The downstream API must expose a scope
   (`api://<client-id>/access_as_user`) and list the Bot app's client ID as a
   known client application.

## Run Locally

```bash
cd src/TeamsBot
dotnet run
# Bot listens on http://localhost:5030/api/messages
```

Open **Bot Framework Emulator** → connect to `http://localhost:5030/api/messages`.

> **Note:** OAuth Prompt sign-in requires the Azure Bot OAuth connection even during
> local testing. Use a magic code flow in the Emulator.

## Deploy & Sideload into Teams

1. Publish the bot to Azure App Service (or Container Apps).
2. Set the messaging endpoint in the Azure Bot resource to
   `https://<your-host>/api/messages`.
3. Update `src/TeamsBot/Manifest/manifest.json` — replace `<BOT_APP_ID>` with the
   real app ID.
4. Replace `outline.png` and `color.png` placeholder icons in the Manifest folder.
5. Zip the Manifest folder contents and sideload into Teams
   (**Apps → Upload a custom app**).

## What This Proves

- **Teams → Bot Framework auth** — the Bot Connector validates inbound activities.
- **OAuth Prompt** — users sign in interactively inside Teams.
- **OBO token exchange** — the bot exchanges the user's token for a downstream API token.
- **Enterprise API call** — the bot calls `GET /api/me` with the OBO token and renders
  results in an Adaptive Card.
