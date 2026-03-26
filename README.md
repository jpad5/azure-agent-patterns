# Azure Agent Architecture Patterns — Executable Code Samples

This repository contains **4 minimal, deployable .NET 8 code samples** that demonstrate the agent architecture patterns described in the *Azure Agent Architectures Comparison Guide*. Each sample proves the end-to-end flow: **user authentication → agent orchestration → OBO token exchange → enterprise API call**.

---

## Table of Contents

| # | Pattern | Folder | Key Components |
|---|---------|--------|----------------|
| 1 | Azure Hosted Agent Service | [`01-hosted-agent-service/`](./01-hosted-agent-service/) | Razor frontend + ASP.NET agent service + M365 Agents SDK |
| 2 | Bot Service (Teams) | [`02-botservice-teams/`](./02-botservice-teams/) | Bot Framework v4 + OAuth Prompt + Teams manifest |
| 3 | Bot Service (Direct Line) | [`03-botservice-directline/`](./03-botservice-directline/) | Bot Framework v4 + Web Chat + token exchange |
| 4 | Agents SDK (Direct-to-Engine) | [`04-agents-sdk-direct-to-engine/`](./04-agents-sdk-direct-to-engine/) | CopilotStudioClient + Custom Action Endpoint |

### Shared Components

- **`shared/enterprise-api/`** — Mock Enterprise API that validates OBO tokens and returns sample data. Used by all four patterns as the downstream resource.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) + [azd CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- An Azure subscription
- A Microsoft Entra ID tenant
- *(Pattern 4 only)* A Copilot Studio environment with a published agent

---

## Quick Start

```bash
cd 01-hosted-agent-service
azd up
```

Each pattern folder contains its own `README.md` with detailed setup instructions, an `infra/` directory with Bicep templates, and an `azure.yaml` for `azd` deployment.

---

## Architecture Comparison Guide

See [`docs/`](./docs/) for the full architecture comparison guide that explains the trade-offs between each pattern.

> ⚠️ **Note:** Some features of the Microsoft 365 Agents SDK (e.g., Direct-to-Engine, CopilotStudioClient) may be in preview. Check [Microsoft Learn](https://learn.microsoft.com/) for current GA status.
