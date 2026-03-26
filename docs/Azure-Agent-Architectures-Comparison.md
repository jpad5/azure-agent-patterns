# Azure Agent Architectures — Comparison Guide

> A side-by-side comparison of four approaches to building AI agent experiences on Azure:  
> Azure Hosted Agent Service, Azure Bot Service (Teams), Azure Bot Service (Direct Line), and Microsoft 365 Agents SDK (Direct-to-Engine).

> **Legend:** Solid arrows (`->>`) = requests/calls · Dashed arrows (`-->>`) = responses/returns  
> Colored blocks group the 5 common phases across all diagrams:  
> 🔵 Authentication · 🟢 Request Submission · 🟡 Orchestration · 🟣 OBO / API Call · 🔴 Response Delivery

> **⚠️ Note:** Some features of the Microsoft 365 Agents SDK (e.g., Direct-to-Engine, CopilotStudioClient) may be in **preview** at the time of reading. Check [Microsoft Learn](https://learn.microsoft.com/microsoft-365/agents-sdk/agents-sdk-overview) for current availability and GA status.

---

## 1. Azure Hosted Agent Service

A custom-built agent service hosted on Azure (e.g., App Service, Container Apps) that uses the Microsoft 365 Agents SDK server-side and delegates orchestration to Copilot Studio. The frontend (Teams, Portal, or custom UI) authenticates the user and passes the JWT directly.

> **Terminology note:** "Azure Hosted Agent Service" is an **architectural pattern** — not a specific Azure product. It refers to any custom-built agent service you host on Azure compute.

**Best for:** Teams/Portal-first deployments where you own the agent service and orchestration logic.

```mermaid
sequenceDiagram
    autonumber

    participant U as User
    participant FE as Frontend App<br/>(Teams / Portal)
    participant EID as Microsoft Entra ID
    participant AHS as Azure Hosted<br/>Agent Service
    participant SDK as Microsoft 365<br/>Agents SDK
    participant CS as Microsoft<br/>Copilot Studio
    participant API as Internal<br/>Enterprise API

    rect rgb(219, 234, 254)
    Note over U, EID: Phase 1 — Authentication
    U->>FE: Sign In
    FE->>EID: Authenticate User
    Note right of EID: Conditional Access<br/>Policies Enforced
    EID-->>FE: User Access Token (JWT)
    end

    rect rgb(220, 252, 231)
    Note over FE, AHS: Phase 2 — Request Submission
    FE->>AHS: Prompt + User Token
    Note over AHS: Validate JWT<br/>(signature, claims, scopes)
    end

    rect rgb(254, 243, 199)
    Note over AHS, CS: Phase 3 — Agent Orchestration
    AHS->>SDK: Send Agent Request
    SDK->>CS: Forward Prompt
    CS-->>SDK: Tool Invocation Required
    SDK-->>AHS: Call Tool Endpoint
    end

    rect rgb(237, 220, 255)
    Note over EID, API: Phase 4 — Delegated API Call (OBO Flow)
    AHS->>EID: OBO Token Exchange
    EID-->>AHS: Delegated Access Token
    AHS->>API: Call API using OBO Token
    API-->>AHS: API Response
    end

    rect rgb(254, 226, 226)
    Note over U, CS: Phase 5 — Response Delivery
    AHS-->>SDK: Tool Response
    SDK->>CS: Provide Tool Output
    CS-->>SDK: Generated Agent Response
    SDK-->>AHS: Return Agent Response
    AHS-->>FE: Final Response
    FE-->>U: Display Response
    end
```

---

## 2. Azure Bot Service — Teams Channel

The traditional Bot Framework pattern for Teams-first experiences. Messages flow through the Bot Framework Connector Service, and the bot triggers an OAuth Prompt for user sign-in.

**Best for:** Teams-first distribution where the bot is the primary interaction surface.

```mermaid
sequenceDiagram
    autonumber

    participant U as User
    participant Teams as Microsoft Teams<br/>(Channel)
    participant BFC as Bot Framework<br/>Connector Service
    participant EID as Microsoft Entra ID
    participant Bot as Azure Bot Service<br/>(Web App)
    participant CS as Microsoft<br/>Copilot Studio
    participant API as Internal<br/>Enterprise API

    rect rgb(219, 234, 254)
    Note over U, Bot: Phase 1 — Message Ingress + Bot Auth
    U->>Teams: Send Message
    Teams->>BFC: Forward Activity
    BFC->>Bot: POST /api/messages
    Note over Bot: Validate Bot Framework<br/>Token (service-to-service)
    end

    rect rgb(220, 252, 231)
    Note over U, EID: Phase 2 — User Authentication (OAuth Prompt)
    Bot-->>BFC: Send OAuthCard
    BFC-->>Teams: Display Sign-In Card
    Teams-->>U: Prompt Sign In
    U->>EID: Authenticate
    Note right of EID: Conditional Access<br/>Policies Enforced
    EID-->>Bot: User Token (via Token Store)
    end

    rect rgb(254, 243, 199)
    Note over Bot, CS: Phase 3 — Agent Orchestration
    Bot->>CS: Forward Prompt
    CS-->>Bot: Tool Invocation Required
    end

    rect rgb(237, 220, 255)
    Note over EID, API: Phase 4 — Delegated API Call (OBO Flow)
    Bot->>EID: OBO Token Exchange
    EID-->>Bot: Delegated Access Token
    Bot->>API: Call API using OBO Token
    API-->>Bot: API Response
    end

    rect rgb(254, 226, 226)
    Note over U, CS: Phase 5 — Response Delivery
    Bot->>CS: Provide Tool Output
    CS-->>Bot: Generated Agent Response
    Bot->>BFC: Reply Activity
    BFC->>Teams: Forward Reply
    Teams-->>U: Display Response
    end
```

---

## 3. Azure Bot Service — Custom Website (Direct Line)

When users interact via a custom website or UI app, Azure Bot Service uses the Direct Line channel. The website embeds the Web Chat control or calls the Direct Line REST/WebSocket API. A server-side secret-to-token exchange bootstraps the session.

**Best for:** Custom UI with existing Bot Framework investment, or when Agents SDK doesn't support your scenario (e.g., service principal tokens).

```mermaid
sequenceDiagram
    autonumber

    participant U as User
    participant WEB as Custom Website<br/>(Web Chat Control)
    participant WBE as Website Backend
    participant DL as Direct Line<br/>Service
    participant EID as Microsoft Entra ID
    participant Bot as Azure Bot Service<br/>(Web App)
    participant CS as Microsoft<br/>Copilot Studio
    participant API as Internal<br/>Enterprise API

    rect rgb(219, 234, 254)
    Note over U, DL: Phase 1 — Session Bootstrap
    U->>WEB: Open Chat Widget
    WEB->>WBE: Request Direct Line Token
    WBE->>DL: Exchange Secret for Token<br/>(server-to-server)
    DL-->>WBE: Direct Line Token
    WBE-->>WEB: Direct Line Token
    WEB->>DL: Open WebSocket Connection
    end

    rect rgb(220, 252, 231)
    Note over U, EID: Phase 2 — User Authentication (choose one)
    Note over WEB, WBE: Option A: Backchannel<br/>(website already has user JWT,<br/>sends via event activity)
    WEB->>DL: Send event: user token (backchannel)
    DL->>Bot: Forward event activity
    Note over Bot: Store user token<br/>for this conversation
    Note over WEB, EID: Option B: OAuth Prompt<br/>(bot triggers sign-in card)
    Bot-->>DL: Send OAuthCard
    DL-->>WEB: Display Sign-In Card
    U->>EID: Authenticate (popup)
    Note right of EID: Conditional Access<br/>Policies Enforced
    EID-->>Bot: User Token (via Token Store)
    end

    rect rgb(254, 243, 199)
    Note over U, CS: Phase 3 — User Sends Prompt
    U->>WEB: Type Message
    WEB->>DL: Send Activity (message)
    DL->>Bot: POST /api/messages
    Note over Bot: Validate Direct Line<br/>Token + User Identity
    Bot->>CS: Forward Prompt
    CS-->>Bot: Tool Invocation Required
    end

    rect rgb(237, 220, 255)
    Note over EID, API: Phase 4 — Delegated API Call (OBO Flow)
    Bot->>EID: OBO Token Exchange
    EID-->>Bot: Delegated Access Token
    Bot->>API: Call API using OBO Token
    API-->>Bot: API Response
    end

    rect rgb(254, 226, 226)
    Note over U, CS: Phase 5 — Response Delivery
    Bot->>CS: Provide Tool Output
    CS-->>Bot: Generated Agent Response
    Bot-->>DL: Reply Activity
    DL-->>WEB: Forward Reply (WebSocket)
    WEB-->>U: Display Response
    end
```

---

## 4. Microsoft 365 Agents SDK — Direct-to-Engine (with Third-Party IDP)

The modern approach: the Agents SDK `CopilotStudioClient` connects **directly to Copilot Studio** (Direct-to-Engine) — no Bot Connector, no Direct Line. Copilot Studio is the **orchestrator** that calls your tool endpoints. User authentication flows through a **federated third-party IDP** (e.g., Okta, Ping, Auth0) via Entra ID.

**Best for:** Modern custom apps, Copilot Studio-orchestrated agents, enterprises with third-party IDPs.

```mermaid
sequenceDiagram
    autonumber

    participant U as User
    participant APP as Custom App<br/>(Agents SDK Client)
    participant IDP as Third-Party IDP<br/>(Okta / Ping / Auth0)
    participant EID as Microsoft Entra ID
    participant CS as Microsoft<br/>Copilot Studio
    participant Tool as Custom Action<br/>Endpoint
    participant API as Internal<br/>Enterprise API

    rect rgb(219, 234, 254)
    Note over U, EID: Phase 1 — Federated User Authentication
    U->>APP: Open App / Sign In
    APP->>EID: MSAL Sign-In Request
    EID->>IDP: Federated Redirect (SAML / OIDC)
    U->>IDP: Authenticate (credentials + MFA)
    IDP-->>EID: SAML Assertion / OIDC Token
    Note over EID: Validate Federation Trust<br/>+ Apply Conditional Access
    EID-->>APP: Access Token<br/>(Copilot Studio.Copilots.Invoke scope)
    end

    rect rgb(220, 252, 231)
    Note over U, CS: Phase 2 — Agent Request (Direct-to-Engine)
    Note over APP: CopilotStudioClient initialized<br/>with connection string
    U->>APP: Enter Prompt
    APP->>CS: Send Activity<br/>(Direct-to-Engine via Agents SDK)
    end

    rect rgb(254, 243, 199)
    Note over CS, Tool: Phase 3 — Orchestration + Tool Invocation
    Note over CS: Process prompt and<br/>plan required actions
    CS->>Tool: Invoke Action<br/>(with user context)
    end

    rect rgb(237, 220, 255)
    Note over EID, API: Phase 4 — Delegated API Call (OBO Flow)
    Tool->>EID: OBO Token Exchange
    EID-->>Tool: Delegated Access Token
    Tool->>API: Call API using OBO Token
    API-->>Tool: API Response
    end

    rect rgb(254, 226, 226)
    Note over U, Tool: Phase 5 — Response Delivery
    Tool-->>CS: Action Response
    Note over CS: Compose final response<br/>from action results
    CS-->>APP: Reply Activity<br/>(Direct-to-Engine via Agents SDK)
    APP-->>U: Display Response
    end
```

---

## Master Comparison Table

| Aspect | Hosted Agent Svc | Bot Svc (Teams) | Bot Svc (Direct Line) | Agents SDK (Direct-to-Engine) |
|--------|:---:|:---:|:---:|:---:|
| **Total steps** | 18 | 19 | 26 (both auth options shown) | 16 |
| **Participants** | 7 | 7 | 8 | 7 |
| **Extra hops** | 0 | +2 (Connector) | +2 (DL + Backend) | 0 |
| **Channel layer** | Direct HTTP | Teams + Connector | Direct Line | Agents SDK protocol |
| **User auth** | Frontend SSO (JWT) | OAuth Prompt (card) | Backchannel / OAuth | Federated IDP → Entra ID |
| **Third-party IDP** | Custom integration | Not native | Not native | **Federated via Entra ID** |
| **Session bootstrap** | None | None | Secret → Token | Connection string (config) |
| **Orchestrator** | Agent Service + SDK | Bot code | Bot code | **Copilot Studio** |
| **Who calls tools?** | Agent Service | Bot | Bot | **Copilot Studio** |
| **OBO performed by** | Agent Service | Bot | Bot | Custom Action Endpoint |
| **Custom UI** | ✅ Full control | ❌ Teams only | ✅ Web Chat | ✅ Full control |
| **SDK in client app** | None (HTTP) | N/A | Web Chat (React) | Agents SDK (.NET/JS/Python) |

---

## Decision Guide

```mermaid
%%{init: {'theme': 'base', 'themeVariables': { 'primaryColor': '#0078d4', 'primaryTextColor': '#fff', 'primaryBorderColor': '#005a9e', 'lineColor': '#374151', 'secondaryColor': '#e8f4fd', 'tertiaryColor': '#f0fdf4', 'fontSize': '14px' }}}%%
flowchart TD
    START(["Where do users interact?"])
    START --> TEAMS["Microsoft Teams"]
    START --> CUSTOM["Custom UI / Website"]
    START --> BOTH["Both"]

    TEAMS --> REC_TEAMS["<b>Azure Bot Service</b><br/><i>Teams Channel</i>"]
    BOTH --> REC_HOSTED["<b>Azure Hosted Agent Service</b><br/><i>Covers Teams + Custom UI</i>"]

    CUSTOM --> ORCH(["Who orchestrates the agent logic?"])

    ORCH --> LOWCODE["Copilot Studio<br/><i>(low-code)</i>"]
    ORCH --> PROCODE["Your own code<br/><i>(pro-code)</i>"]

    LOWCODE --> REC_SDK["<b>Agents SDK</b><br/><i>Direct-to-Engine</i>"]
    PROCODE --> REC_HOSTED2["<b>Azure Hosted Agent Service</b><br/><i>Custom service + own logic</i>"]

    REC_SDK --> LEGACY{{"Need legacy Direct Line<br/>compatibility?"}}
    REC_HOSTED2 --> LEGACY

    LEGACY -- "✅ Yes" --> REC_DL["<b>Azure Bot Service</b><br/><i>Direct Line</i>"]
    LEGACY -- "❌ No" --> DONE(["Use recommended<br/>architecture above"])

    classDef question fill:#0078d4,stroke:#005a9e,color:#fff,font-weight:bold
    classDef answer fill:#e8f4fd,stroke:#0078d4,color:#1a1a2e
    classDef recommend fill:#059669,stroke:#047857,color:#fff,font-weight:bold
    classDef legacy fill:#f59e0b,stroke:#d97706,color:#1a1a2e,font-weight:bold
    classDef done fill:#6b7280,stroke:#4b5563,color:#fff

    class START,ORCH question
    class TEAMS,CUSTOM,BOTH,LOWCODE,PROCODE answer
    class REC_TEAMS,REC_HOSTED,REC_SDK,REC_HOSTED2 recommend
    class REC_DL,LEGACY legacy
    class DONE done
```

---

## References

- [Microsoft 365 Agents SDK Overview](https://learn.microsoft.com/microsoft-365/agents-sdk/agents-sdk-overview)
- [Activity Protocol](https://learn.microsoft.com/microsoft-365/agents-sdk/activity-protocol)
- [Integrate with Web/Native Apps via Agents SDK](https://learn.microsoft.com/microsoft-copilot-studio/publication-integrate-web-or-native-app-m365-agents-sdk)
- [Azure Bot Service — Connect to Direct Line](https://learn.microsoft.com/azure/bot-service/bot-service-channel-connect-directline)
- [Azure Bot Service — Connect to Web Chat](https://learn.microsoft.com/azure/bot-service/bot-service-channel-connect-webchat)
- [Entra ID — SAML/WS-Fed Federation with External IDPs](https://learn.microsoft.com/entra/external-id/direct-federation-overview)
- [Configure OAuth in Agents SDK (.NET)](https://learn.microsoft.com/microsoft-365/agents-sdk/configure-oauth)
