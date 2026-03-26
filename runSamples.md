# Running Pattern 1 Ã¢â‚¬â€ Azure Hosted Agent Service

## Environment

- **OS:** Windows
- **.NET SDK:** 10.0.102
- **Azure Subscription:** <YOUR_SUBSCRIPTION_NAME> (`<YOUR_SUBSCRIPTION_ID>`)
- **Tenant ID:** `<YOUR_TENANT_ID>`

---

## Step 1: Create Entra ID App Registrations

### 1a. Register Enterprise API

```powershell
$enterpriseApi = az ad app create --display-name "AgentPatterns-EnterpriseAPI" --sign-in-audience "AzureADMyOrg" -o json | ConvertFrom-Json
$eapiAppId = $enterpriseApi.appId
# Output: <ENTERPRISE_API_CLIENT_ID>
```

### 1b. Configure Enterprise API Ã¢â‚¬â€ Identifier URI & Scope

> **Important:** Use the Microsoft Graph REST API to create OAuth2 scopes.
> The `az ad app update --set api.oauth2PermissionScopes=...` command can silently
> fail on Windows due to PowerShell JSON escaping issues.

```powershell
$eapiScopeId = [guid]::NewGuid().ToString()
az ad app update --id $eapiAppId --identifier-uris "api://$eapiAppId"

# Get the object ID (different from appId) needed for Graph API
$eapiObjectId = az ad app show --id $eapiAppId --query id -o tsv

# Create the scope via Microsoft Graph REST API
$body = @{
    api = @{
        oauth2PermissionScopes = @(
            @{
                id = $eapiScopeId
                adminConsentDescription = "Access Enterprise API as user"
                adminConsentDisplayName = "access_as_user"
                isEnabled = $true
                type = "User"
                userConsentDescription = "Access Enterprise API as you"
                userConsentDisplayName = "access_as_user"
                value = "access_as_user"
            }
        )
    }
} | ConvertTo-Json -Depth 5
$body | Out-File -FilePath "$env:TEMP\eapi-scope.json" -Encoding utf8
az rest --method PATCH `
  --url "https://graph.microsoft.com/v1.0/applications/$eapiObjectId" `
  --headers "Content-Type=application/json" `
  --body "@$env:TEMP\eapi-scope.json"

# Verify the scope was created
az ad app show --id $eapiAppId --query "api.oauth2PermissionScopes[].value" -o json
# Expected output: ["access_as_user"]
```

### 1c. Register Agent Service

```powershell
$agentSvc = az ad app create --display-name "AgentPatterns-AgentService" --sign-in-audience "AzureADMyOrg" -o json | ConvertFrom-Json
$agentAppId = $agentSvc.appId
# Output: <AGENT_SERVICE_CLIENT_ID>
```

### 1d. Configure Agent Service Ã¢â‚¬â€ Identifier URI & Scope

```powershell
$agentScopeId = [guid]::NewGuid().ToString()
az ad app update --id $agentAppId --identifier-uris "api://$agentAppId"

# Get the object ID needed for Graph API
$agentObjectId = az ad app show --id $agentAppId --query id -o tsv

# Create the scope via Microsoft Graph REST API
$body = @{
    api = @{
        oauth2PermissionScopes = @(
            @{
                id = $agentScopeId
                adminConsentDescription = "Access Agent Service as user"
                adminConsentDisplayName = "access_as_user"
                isEnabled = $true
                type = "User"
                userConsentDescription = "Access Agent Service as you"
                userConsentDisplayName = "access_as_user"
                value = "access_as_user"
            }
        )
    }
} | ConvertTo-Json -Depth 5
$body | Out-File -FilePath "$env:TEMP\agent-scope.json" -Encoding utf8
az rest --method PATCH `
  --url "https://graph.microsoft.com/v1.0/applications/$agentObjectId" `
  --headers "Content-Type=application/json" `
  --body "@$env:TEMP\agent-scope.json"

# Verify the scope was created
az ad app show --id $agentAppId --query "api.oauth2PermissionScopes[].value" -o json
# Expected output: ["access_as_user"]
```

### 1e. Add Enterprise API Permission to Agent Service (for OBO)

```powershell
az ad app permission add --id $agentAppId --api $eapiAppId --api-permissions "$eapiScopeId=Scope"
```

### 1f. Create Agent Service Client Secret

```powershell
$secretResult = az ad app credential reset --id $agentAppId --display-name "dev-secret" --years 1 -o json | ConvertFrom-Json
$agentSecret = $secretResult.password
# Agent Service secret created (length: 40)
```

### 1g. Register Frontend App

```powershell
$frontendApp = az ad app create --display-name "AgentPatterns-FrontendApp" --sign-in-audience "AzureADMyOrg" --web-redirect-uris "http://localhost:5010/signin-oidc" -o json | ConvertFrom-Json
$frontendAppId = $frontendApp.appId
# Output: <FRONTEND_CLIENT_ID>
```

### 1h. Add Agent Service Permission to Frontend App & Create Secret

```powershell
az ad app permission add --id $frontendAppId --api $agentAppId --api-permissions "$agentScopeId=Scope"

$frontendSecretResult = az ad app credential reset --id $frontendAppId --display-name "dev-secret" --years 1 -o json | ConvertFrom-Json
$frontendSecret = $frontendSecretResult.password
# Frontend secret created (length: 40)
```

### 1i. Create Service Principals & Grant Admin Consent

```powershell
az ad sp create --id $agentAppId
az ad sp create --id $frontendAppId
az ad sp create --id $eapiAppId
Start-Sleep -Seconds 5
az ad app permission admin-consent --id $agentAppId
az ad app permission admin-consent --id $frontendAppId
```

### 1j. Add Power Platform API Permission (for Copilot Studio Direct-to-Engine)

The Agent Service needs the `CopilotStudio.Copilots.Invoke` delegated permission
on the Power Platform API to call Copilot Studio agents via the Direct-to-Engine API.

```powershell
# Power Platform API app ID (well-known)
$ppApiAppId = "8578e004-a5c6-46e7-913e-12f58912df43"

# Find the CopilotStudio.Copilots.Invoke scope ID from the service principal
$ppSp = az ad sp show --id $ppApiAppId -o json | ConvertFrom-Json
$invokeScope = ($ppSp.oauth2PermissionScopes | Where-Object { $_.value -eq "CopilotStudio.Copilots.Invoke" }).id

# Add the delegated permission to the Agent Service app
az ad app permission add --id $agentAppId --api $ppApiAppId --api-permissions "$invokeScope=Scope"

# Grant admin consent
Start-Sleep -Seconds 5
az ad app permission admin-consent --id $agentAppId
```

---

## Step 2: Update appsettings.json Files

### shared/enterprise-api/appsettings.json

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<YOUR_TENANT_ID>",
    "ClientId": "<ENTERPRISE_API_CLIENT_ID>",
    "Audience": "api://<ENTERPRISE_API_CLIENT_ID>"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

### 01-hosted-agent-service/src/AgentService/appsettings.json

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<YOUR_TENANT_ID>",
    "ClientId": "<AGENT_SERVICE_CLIENT_ID>",
    "ClientSecret": "<AGENT_SERVICE_CLIENT_SECRET>",
    "Audience": "api://<AGENT_SERVICE_CLIENT_ID>"
  },
  "CopilotStudio": {
    "TokenEndpoint": "<YOUR_COPILOT_STUDIO_CONVERSATIONS_URL>",
    "BotId": "<YOUR_BOT_ID>"
  },
  "EnterpriseApi": {
    "BaseUrl": "http://localhost:5050",
    "Scope": "api://<ENTERPRISE_API_CLIENT_ID>/access_as_user"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

> **CopilotStudio:TokenEndpoint** Ã¢â‚¬â€ this is the Direct-to-Engine conversations URL
> for your Copilot Studio agent. Get it from Copilot Studio Ã¢â€ â€™ your agent Ã¢â€ â€™
> Settings Ã¢â€ â€™ Advanced Ã¢â€ â€™ Direct-to-Engine endpoint. The URL looks like:
> `https://<env>.environment.api.powerplatform.com/copilotstudio/dataverse-backed/authenticated/bots/<bot_schema_name>/conversations?api-version=2022-03-01-preview`

### 01-hosted-agent-service/src/FrontendApp/appsettings.json

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<YOUR_TENANT_ID>",
    "ClientId": "<FRONTEND_CLIENT_ID>",
    "ClientSecret": "<FRONTEND_CLIENT_SECRET>",
    "CallbackPath": "/signin-oidc",
    "SignedOutCallbackPath": "/signout-callback-oidc"
  },
  "AgentService": {
    "BaseUrl": "http://localhost:5020",
    "Scope": "api://<AGENT_SERVICE_CLIENT_ID>/access_as_user"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

---

## Step 3: Build All Projects

```powershell
dotnet build shared/enterprise-api/enterprise-api.csproj
# Build succeeded. 2 Warning(s), 0 Error(s)

dotnet build 01-hosted-agent-service/src/AgentService/AgentService.csproj
# Build succeeded. 0 Warning(s), 0 Error(s)

dotnet build 01-hosted-agent-service/src/FrontendApp/FrontendApp.csproj
# Build succeeded. 0 Warning(s), 0 Error(s)
```

---

## Step 4: Start All 3 Services

```powershell
# Terminal 1 Ã¢â‚¬â€ Enterprise API (port 5050)
cd shared/enterprise-api
dotnet run

# Terminal 2 Ã¢â‚¬â€ Agent Service (port 5020)
cd 01-hosted-agent-service/src/AgentService
dotnet run

# Terminal 3 Ã¢â‚¬â€ Frontend App (port 5010)
cd 01-hosted-agent-service/src/FrontendApp
dotnet run
```

---

## Step 5: Verify Services Are Running

```powershell
# Enterprise API health check
Invoke-WebRequest -Uri "http://localhost:5050/health" -UseBasicParsing
# Returns: {"status":"healthy"}

# Agent Service (no root endpoint Ã¢â‚¬â€ 404 expected, confirms it's running)
Invoke-WebRequest -Uri "http://localhost:5020" -UseBasicParsing
# Returns: 404 NotFound (expected Ã¢â‚¬â€ only /api/agent/invoke is exposed)

# Frontend App (redirects to Entra ID login Ã¢â‚¬â€ 302 expected)
Invoke-WebRequest -Uri "http://localhost:5010" -UseBasicParsing -MaximumRedirection 0
# Returns: 302 (redirect to login.microsoftonline.com)
```

---

## Step 6: Open in Browser

```powershell
Start-Process "http://localhost:5010"
```

Navigate to `http://localhost:5010`. Sign in with your Entra ID account, then submit a prompt. The request flows through:

**Frontend SSO Ã¢â€ â€™ Agent Service (JWT validation) Ã¢â€ â€™ Copilot Studio (Direct-to-Engine) Ã¢â€ â€™ OBO Ã¢â€ â€™ Enterprise API**

---

## App Registration Summary

| App | Client ID | Purpose |
|-----|-----------|---------|
| AgentPatterns-EnterpriseAPI | `<ENTERPRISE_API_CLIENT_ID>` | Downstream API (validates OBO tokens) |
| AgentPatterns-AgentService | `<AGENT_SERVICE_CLIENT_ID>` | Agent orchestrator (JWT validation, Copilot Studio Direct-to-Engine, OBO exchange) |
| AgentPatterns-FrontendApp | `<FRONTEND_CLIENT_ID>` | Razor Pages frontend (OIDC sign-in) |

| Service | Port |
|---------|------|
| Enterprise API | 5050 |
| Agent Service | 5020 |
| Frontend App | 5010 |

---

## Troubleshooting

### Error: `AADSTS650053 Ã¢â‚¬â€ scope 'access_as_user' doesn't exist on the resource`

**Cause:** The `az ad app update --set api.oauth2PermissionScopes=...` command silently
fails on Windows PowerShell due to JSON escaping issues. The scope is never created
even though the command exits without error.

**Fix:** Use the Microsoft Graph REST API instead (as shown in Steps 1b and 1d above).
To verify whether scopes exist on an app:

```powershell
# Check if the scope is actually registered
az ad app show --id <APP_CLIENT_ID> --query "api.oauth2PermissionScopes[].value" -o json
# If this returns [], the scope was never created
```

To fix after the fact:

```powershell
$scopeId = [guid]::NewGuid().ToString()
$objectId = az ad app show --id <APP_CLIENT_ID> --query id -o tsv
$body = @{
    api = @{
        oauth2PermissionScopes = @(
            @{
                id = $scopeId
                adminConsentDescription = "Access as user"
                adminConsentDisplayName = "access_as_user"
                isEnabled = $true
                type = "User"
                userConsentDescription = "Access as you"
                userConsentDisplayName = "access_as_user"
                value = "access_as_user"
            }
        )
    }
} | ConvertTo-Json -Depth 5
$body | Out-File -FilePath "$env:TEMP\fix-scope.json" -Encoding utf8
az rest --method PATCH `
  --url "https://graph.microsoft.com/v1.0/applications/$objectId" `
  --headers "Content-Type=application/json" `
  --body "@$env:TEMP\fix-scope.json"

# Then re-grant admin consent
az ad app permission admin-consent --id <APP_CLIENT_ID>
```

### Error: `admin-consent` fails

Service principals must exist before admin consent can be granted:

```powershell
az ad sp create --id <APP_CLIENT_ID>   # create SP first
Start-Sleep -Seconds 5                  # wait for propagation
az ad app permission admin-consent --id <APP_CLIENT_ID>
```

### Error: 400 Bad Request when sending a message to Copilot Studio

**Cause:** The Direct-to-Engine execute turn endpoint expects the user message wrapped
in an `activity` property. Sending a raw Bot Framework Activity object (e.g.,
`{ "type": "message", "text": "..." }`) at the top level is rejected with 400.

**Fix:** Wrap the message inside an `activity` object:

```json
{
  "activity": {
    "type": "message",
    "text": "your prompt here"
  }
}
```

In C#:
```csharp
var turnPayload = new
{
    activity = new
    {
        type = "message",
        text = userPrompt
    }
};
await httpClient.PostAsJsonAsync(turnUrl, turnPayload);
```

### Error: 404 Not Found when posting to `/conversations/{id}/activities`

**Cause:** The Direct-to-Engine API does not have an `/activities` sub-path. Unlike
the Bot Framework Direct Line API, the execute turn endpoint is the conversation
URL itself.

**Fix:** Post to `/conversations/{conversationId}` (not `/conversations/{id}/activities`):

```
POST https://<env>.environment.api.powerplatform.com/copilotstudio/dataverse-backed/authenticated/bots/<bot>/conversations/{conversationId}?api-version=2022-03-01-preview
```

### Error: Start conversation returns a greeting but user message is never sent

**Cause:** When starting a conversation, Copilot Studio may return an `activities`
array containing a bot greeting. If your code extracts this as the agent response
without checking whether it's actually the greeting vs. the answer to the user's
question, the user message is never sent.

**Fix:** After starting a conversation, check if the activities contain a bot message.
If they do, this is typically a greeting Ã¢â‚¬â€ compare or flag it, then proceed to
send the user's actual message via the execute turn endpoint.
