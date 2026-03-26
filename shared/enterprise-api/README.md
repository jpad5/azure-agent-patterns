# Enterprise API (Mock)

Shared mock enterprise API used by all 4 agent pattern samples. It validates OBO (On-Behalf-Of) tokens issued by Entra ID and returns the decoded user claims.

## Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/me` | Bearer (OBO) | Returns decoded claims from the validated token |
| GET | `/health` | None | Health check |

## Run locally

```bash
dotnet run
```

The API starts at **http://localhost:5050**.

## Test

```bash
curl http://localhost:5050/health
```

Swagger UI is available at http://localhost:5050/swagger.

## Configuration

Update `appsettings.json` with your Entra ID tenant and app registration values:

| Key | Description |
|-----|-------------|
| `TenantId` | Your Azure AD tenant ID |
| `ClientId` | App registration client ID for this API |
| `Audience` | Typically `api://<ClientId>` |

The API exposes the scope `api://<client-id>/access_as_user` that agent services request via the OBO flow.
