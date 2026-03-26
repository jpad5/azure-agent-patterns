# Pattern 1 — Azure Hosted Agent Service

![Architecture](../../docs/diagrams/01-HostedAgentService.png)

## Overview

This pattern demonstrates an **Azure-hosted agent service** where a frontend web
application authenticates the user via Entra ID SSO, then delegates prompt
processing to a backend Agent Service. The Agent Service validates the user's JWT,
orchestrates a call to Copilot Studio (simulated via the M365 Agents SDK), and
performs an **On-Behalf-Of (OBO)** token exchange to call a shared Enterprise API
as the signed-in user.

## Components

| Component | Port | Description |
|---|---|---|
| **FrontendApp** | `5010` | Razor Pages app with MSAL / OpenID Connect SSO |
| **AgentService** | `5020` | ASP.NET Core API — JWT validation, Copilot Studio orchestration (simulated), OBO |
| **Enterprise API** | `5050` | Shared downstream API (see `shared/enterprise-api`) |

## Auth Flow

```
User → FrontendApp (OIDC sign-in) → acquires token for AgentService scope
     → POST /api/agent/invoke (Bearer token)
     → AgentService validates JWT
     → (simulated) Copilot Studio call via M365 Agents SDK
     → OBO exchange: user token → Enterprise API token
     → GET /api/me on Enterprise API
     → combined response returned to frontend
```

## Prerequisites

1. **.NET 8 SDK** (or later)
2. **Three Entra ID app registrations:**
   - **Frontend App** — redirect URI `http://localhost:5010/signin-oidc`
   - **Agent Service** — expose an API scope `access_as_user`; grant the Frontend
     App permission to call it; add a client secret
   - **Enterprise API** — expose an API scope `access_as_user`; grant the Agent
     Service permission to call it via OBO

## App Registration Setup (Brief)

1. Register **Enterprise API** → expose scope `api://<ENTERPRISE_API_CLIENT_ID>/access_as_user`.
2. Register **Agent Service** → expose scope `api://<AGENT_SERVICE_CLIENT_ID>/access_as_user`;
   add API permission for Enterprise API scope; create a client secret.
3. Register **Frontend App** → add API permission for Agent Service scope;
   set redirect URI to `http://localhost:5010/signin-oidc`.
4. Update `appsettings.json` in each project with the corresponding client IDs,
   tenant ID, and secrets.

## How to Run

```bash
# 1. Start the Enterprise API (shared)
cd shared/enterprise-api
dotnet run

# 2. Start the Agent Service
cd 01-hosted-agent-service/src/AgentService
dotnet run

# 3. Start the Frontend App
cd 01-hosted-agent-service/src/FrontendApp
dotnet run

# 4. Open the frontend
# Navigate to http://localhost:5010
```

## What This Proves

- **Frontend SSO** — user signs in via Entra ID; token acquired for Agent Service.
- **JWT validation** — Agent Service validates the token using Microsoft.Identity.Web.
- **Copilot Studio orchestration (simulated)** — shows where M365 Agents SDK calls
  would plug in.
- **OBO token exchange** — Agent Service exchanges the user token for a downstream
  token scoped to the Enterprise API.
- **Enterprise API call** — the user's identity flows through the entire chain.
