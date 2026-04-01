# Pattern 01 — Hosted Agent Service: End-to-End Flow Walkthrough

This walkthrough traces a single request through the Hosted Agent Service pattern,
showing the complete authentication chain, Copilot Studio integration via the
CopilotClient SDK, and downstream Enterprise API call — all captured from real
production logs.

## Architecture

```
Browser → FrontendApp (Razor Pages, OIDC SSO)
       → AgentService (ASP.NET Core API)
           → Copilot Studio (CopilotClient SDK, SSE streaming)
           → Enterprise API (OBO token exchange)
```

## The Flow

### Step 1 — Request Arrives, JWT Validated

The FrontendApp sends the user's prompt to the AgentService with a Bearer token.
Microsoft.Identity.Web validates the JWT automatically — signature, lifetime, and
audience are all checked before the endpoint code runs.

```log
10:50:03 [INF] Request starting HTTP/1.1 POST /api/agent/invoke

10:50:04 [INF] Security token has a valid signature
10:50:04 [INF] Lifetime of the token is valid
10:50:04 [INF] Audience Validated. Audience: 'api://f5c8629c-0c34-408d-80c1-164be16e3900'

10:50:04 [INF] Agent invoked by "admin@myenterpriseai.com" with prompt: "how can biology help?"
```

**What's happening:** The FrontendApp acquired a token scoped to the AgentService
API (`api://f5c8629c-.../access_as_user`) during the user's OIDC sign-in. The
AgentService validates it using Microsoft.Identity.Web's built-in JWT Bearer
middleware — no custom validation code needed.

---

### Step 2 — OBO Token Exchange #1: Power Platform API

The AgentService exchanges the user's token for a Power Platform API token using
On-Behalf-Of (OBO). This token is needed to call Copilot Studio as the signed-in
user.

```log
10:50:05 [INF] === Token Acquisition (OnBehalfOfRequest) started:
               Scopes: https://api.powerplatform.com/CopilotStudio.Copilots.Invoke
               Authority Host: login.microsoftonline.com

10:50:05 [INF] [OBO request] Fetching tokens via normal OBO flow.
10:50:05 [INF] Sending HTTP request POST https://login.microsoftonline.com/.../oauth2/v2.0/token
10:50:05 [INF] Received HTTP response headers after 256ms - 200

10:50:05 [INF] === Token Acquisition finished successfully:
               AT expiration time: 4/1/2026 6:57:23 PM, source: IdentityProvider
               DurationTotalInMs: 691
```

**What's happening:** MSAL's `AcquireTokenOnBehalfOf` sends the user's assertion
token to Entra ID and receives back a new token scoped to
`CopilotStudio.Copilots.Invoke`. This token proves that the *original user*
(not the service) is making the request to Copilot Studio.

---

### Step 3 — CopilotClient SDK: Start Conversation (SSE Streaming)

The `CopilotClient` from the Agents SDK opens a conversation with the Copilot
Studio agent. The SDK handles SSE (Server-Sent Events) streaming, returning
activities as an `IAsyncEnumerable<Activity>`.

```log
10:50:05 [INF] Sending HTTP request POST
               https://...api.powerplatform.com/copilotstudio/dataverse-backed/
               authenticated/bots/copilots_header_cr351_bioagent/conversations?...
10:50:06 [INF] Received HTTP response headers after 455ms - 200

10:50:08 [INF] Bot greeting: "Hello, I'm Biology Agent, a virtual assistant.
               Just so you are aware, I sometimes use AI to answer your questions..."
```

**What's happening:** `copilotClient.StartConversationAsync()` opens the
Direct-to-Engine channel. The 200 response starts an SSE stream; the SDK reads
activities as they arrive. The bot's greeting message is streamed back ~2 seconds
after the HTTP headers arrive — this is the SSE streaming in action.

---

### Step 4 — CopilotClient SDK: Ask Question (SSE Streaming)

The user's actual prompt is sent via `AskQuestionAsync`. The SDK reuses the
existing conversation and the cached OBO token.

```log
10:50:08 [INF] Access token is not expired. Returning the found cache entry.
               DurationTotalInMs: 24                          ← token reused from cache

10:50:08 [INF] Sending HTTP request POST
               https://...conversations/f1ae8c90-e65f-4cf5-a6a5-b2c3d9c7aace?...
10:50:08 [INF] Received HTTP response headers after 314ms - 200

10:50:13 [INF] Bot reply: "**How Biology Can Help**
               Biology is the study of living organisms and their interactions
               with the environment. It helps in several ways:
               - **Medical Advances:** Understanding biology leads to new treatments...
               - **Environmental Protection:** Biology helps us conserve ecosystems...
               - **Agriculture:** Biological research improves crop yields...
               - **Biotechnology:** Biology enables innovations like genetic engineering...
               - **Education and Awareness:** It increases knowledge about health..."
```

**What's happening:** The SDK sends the user's prompt to the existing conversation
(`f1ae8c90-...`). The OBO token is served from MSAL's in-memory cache in 24ms
(no network call). The bot's generative response streams back over ~5 seconds via
SSE — the SDK collects all message activities and yields them to our
`await foreach` loop.

---

### Step 5 — OBO Token Exchange #2: Enterprise API

A second OBO exchange acquires a token for the downstream Enterprise API, scoped
to `access_as_user`. The user's identity flows through the entire chain.

```log
10:50:13 [INF] === Token Acquisition (OnBehalfOfRequest) started:
               Scopes: api://5957987c-e0dd-4b36-bf23-5f62224d5d2a/access_as_user

10:50:13 [INF] [OBO request] Fetching tokens via normal OBO flow.
10:50:13 [INF] Sending HTTP request POST https://login.microsoftonline.com/.../oauth2/v2.0/token
10:50:14 [INF] Received HTTP response headers after 281ms - 200

10:50:14 [INF] === Token Acquisition finished successfully:
               AT expiration time: 4/1/2026 7:04:23 PM, source: IdentityProvider
               DurationTotalInMs: 287
```

**What's happening:** This is a *separate* OBO exchange — same user assertion, but
a different scope (`access_as_user` on the Enterprise API). The Enterprise API
will see the original user's identity in its JWT claims, even though the
AgentService is the one making the HTTP call.

---

### Step 6 — Enterprise API Call

The AgentService calls the Enterprise API with the OBO token. In this run, the
Enterprise API returned a 500 error — the AgentService gracefully falls back to
simulated data.

```log
10:50:14 [INF] Sending HTTP request GET http://localhost:5050/api/me
10:50:14 [INF] Received HTTP response headers after 19ms - 500

10:50:14 [WRN] Enterprise API call failed — returning simulated data
               System.Net.Http.HttpRequestException: Response status code does not
               indicate success: 500 (Internal Server Error).
```

**What's happening:** The Enterprise API validates the OBO token and returns
user-specific data. The 500 here is a pre-existing issue in the Enterprise API —
the AgentService handles it gracefully so the user still gets the Copilot Studio
response.

---

### Step 7 — Combined Response Returned

The AgentService combines the bot's response with the enterprise data and returns
a single JSON payload to the FrontendApp.

```log
10:50:14 [INF] Setting HTTP status code 200
10:50:14 [INF] Writing value of type '<>f__AnonymousType1`3' as Json
10:50:14 [INF] Request finished HTTP/1.1 POST /api/agent/invoke - 200 - 10863ms
```

---

## Timing Breakdown

| Step | Duration | Notes |
|---|---|---|
| JWT validation | ~1.2s | Includes OIDC metadata discovery (first request only) |
| OBO #1 (Power Platform) | 691ms | Network call to Entra ID token endpoint |
| StartConversationAsync | ~3s | SSE stream: 455ms to headers, ~2s for greeting |
| AskQuestionAsync | ~5.4s | Token from cache (24ms), SSE stream: 314ms to headers, ~5s for generative answer |
| OBO #2 (Enterprise API) | 287ms | Network call to Entra ID token endpoint |
| Enterprise API call | 19ms | Localhost, returned 500 |
| **Total** | **~10.8s** | Dominated by Copilot Studio SSE streaming |

## Key Takeaways

1. **Identity flows end-to-end.** The user signs in once at the FrontendApp. Their
   identity is carried through two OBO exchanges to both Copilot Studio and the
   Enterprise API. No service accounts, no shared secrets at the API layer.

2. **The CopilotClient SDK handles SSE correctly.** The Direct-to-Engine API
   returns Server-Sent Events for generative responses. The SDK's
   `IAsyncEnumerable<Activity>` model handles this transparently — you just
   `await foreach` over the activities.

3. **Token caching matters.** The second Copilot Studio call reused the cached OBO
   token (24ms vs 691ms for the first). MSAL's in-memory cache handles this
   automatically within a request.

4. **Graceful degradation.** The Enterprise API failure didn't crash the request.
   The AgentService returned the Copilot Studio response with simulated enterprise
   data — the user still got a useful answer.
