# Pattern 4 — Microsoft 365 Agents SDK (Direct-to-Engine)

## Architecture

```
CustomApp (Console)
    │  MSAL interactive sign-in (federated IDP via Entra ID)
    ▼
Copilot Studio (Direct-to-Engine)
    │  Orchestrates conversation, invokes custom actions
    ▼
ActionEndpoint (ASP.NET Core)
    │  OBO token exchange
    ▼
Enterprise API
```

**Key insight:** Copilot Studio is the *orchestrator*, not an intermediary. The
CustomApp talks directly to the Copilot Studio engine using `CopilotClient` from
the `Microsoft.Agents.CopilotStudio.Client` NuGet package. The SDK handles SSE
streaming and conversation lifecycle. The agent decides when to invoke tools
(custom actions) like the ActionEndpoint.

## Third-Party IDP Support

Federated identity providers (Okta, Ping Identity, Auth0, etc.) work through
**Entra ID federation** — no code changes are required in the application. When a
federated domain is configured in Entra ID, MSAL's interactive sign-in
automatically redirects the user to the third-party IDP.

Configuration steps:
1. Set up the third-party IDP as a federated identity provider in Entra ID.
2. Associate the user's domain with the federation trust.
3. MSAL handles the redirect automatically during `AcquireTokenInteractive`.

## Prerequisites

- .NET 8 SDK
- A Copilot Studio environment with a published agent
- The agent must have a **custom action** configured pointing to the ActionEndpoint
- Entra ID app registrations for both CustomApp and ActionEndpoint
- An Enterprise API running on `http://localhost:5050` (see Pattern 1/2)

## How to Run

### 1. Start the Enterprise API (from another pattern)

```bash
# e.g., from Pattern 1 or 2
dotnet run --project ../01-direct-entra/src/EnterpriseApi
```

### 2. Start the ActionEndpoint

```bash
cd src/ActionEndpoint
dotnet run --urls http://localhost:5060
```

### 3. Configure Copilot Studio

- In Copilot Studio, add a custom action pointing to `http://localhost:5060/api/actions/get-employee-profile`
- Configure the action's authentication to pass through the user's token

### 4. Run CustomApp

```bash
cd src/CustomApp
dotnet run
```

You will be prompted to sign in interactively. After authentication, chat with
the agent:

```
=== Agents SDK Direct-to-Engine Demo ===
Signed in as: user@contoso.com
Type a message (or 'quit' to exit):

You: What's my employee profile?
Agent: [response from Copilot Studio including enterprise API data]
```

## What This Proves

1. **MSAL federated sign-in** — Users from federated IDPs (Okta, Ping, Auth0)
   authenticate seamlessly via Entra ID federation.
2. **Direct-to-Engine via CopilotClient** — The custom app connects directly to
   the Copilot Studio engine using `CopilotClient` from the Agents SDK, with
   full SSE streaming support and proper conversation lifecycle management.
3. **Copilot Studio orchestration** — The agent decides when to invoke tools
   and custom actions as part of the conversation.
4. **Tool invocation → OBO → Enterprise API** — The ActionEndpoint performs
   OBO token exchange to call the Enterprise API on behalf of the user.

## Project Structure

```
04-agents-sdk-direct-to-engine/
├── README.md
└── src/
    ├── CustomApp/               # Console app using Agents SDK
    │   ├── CustomApp.csproj
    │   ├── Program.cs
    │   └── appsettings.json
    └── ActionEndpoint/          # ASP.NET Core action endpoint
        ├── ActionEndpoint.csproj
        ├── Program.cs
        └── appsettings.json
```

## SDK Details

This sample uses `CopilotClient` from the
[`Microsoft.Agents.CopilotStudio.Client`](https://www.nuget.org/packages/Microsoft.Agents.CopilotStudio.Client)
NuGet package (GA). The SDK handles:
- **SSE streaming** — responses are delivered as `IAsyncEnumerable<Activity>`.
- **Conversation lifecycle** — `StartConversationAsync` and `AskQuestionAsync`
  manage conversation state automatically.
- **Token management** — a token provider function is called on demand to
  acquire/refresh tokens via MSAL.
