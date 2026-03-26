using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;

// ---------------------------------------------------------------------------
// Pattern 4 — Agents SDK Direct-to-Engine (with Third-Party IDP)
//
// This console app authenticates the user via MSAL and talks directly to a
// Copilot Studio agent using the M365 Agents SDK (direct-to-engine).
// ---------------------------------------------------------------------------

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var tenantId = config["AzureAd:TenantId"]!;
var clientId = config["AzureAd:ClientId"]!;
var connectionString = config["CopilotStudio:ConnectionString"]!;

// ── 1. Authenticate the user via MSAL interactive sign-in ──────────────
// For federated third-party IDP (Okta, Ping, Auth0): configure Entra ID
// federation. MSAL will automatically redirect to the federated IDP during
// interactive sign-in.

var msalApp = PublicClientApplicationBuilder.Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithRedirectUri("http://localhost")
    .Build();

var scopes = new[] { "api://<COPILOT_STUDIO_SCOPE>/Copilots.Invoke" };

AuthenticationResult authResult;
try
{
    authResult = await msalApp.AcquireTokenInteractive(scopes).ExecuteAsync();
}
catch (MsalException ex)
{
    Console.Error.WriteLine($"Authentication failed: {ex.Message}");
    return;
}

Console.WriteLine();
Console.WriteLine("=== Agents SDK Direct-to-Engine Demo ===");
Console.WriteLine($"Signed in as: {authResult.Account.Username}");

// ── 2. Initialize the Copilot Studio client ────────────────────────────
//
// When the official SDK package is available, replace the simulator with:
//
//   using Microsoft.Agents.CopilotStudio.Client;
//
//   var settings = new CopilotStudioClientSettings
//   {
//       ConnectionString = connectionString,
//       EnvironmentId    = config["CopilotStudio:EnvironmentId"],
//       SchemaName       = config["CopilotStudio:SchemaName"],
//   };
//   var client = new CopilotStudioClient(settings, authResult.AccessToken);
//
//   // Send a message:
//   var response = await client.SendActivityAsync("Hello");
//   Console.WriteLine(response.Text);
//

var client = new CopilotStudioClientSimulator(
    connectionString: connectionString,
    environmentId: config["CopilotStudio:EnvironmentId"]!,
    schemaName: config["CopilotStudio:SchemaName"]!,
    accessToken: authResult.AccessToken);

// ── 3. Interactive chat loop ───────────────────────────────────────────

Console.WriteLine("Type a message (or 'quit' to exit):");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) || input.Equals("quit", StringComparison.OrdinalIgnoreCase))
        break;

    try
    {
        var reply = await client.SendActivityAsync(input);
        Console.WriteLine($"Agent: {reply}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
    }

    Console.WriteLine();
}

Console.WriteLine("Session ended.");

// ===========================================================================
// CopilotStudioClientSimulator
//
// A lightweight stand-in that mimics the real Microsoft.Agents.CopilotStudio
// .Client.CopilotStudioClient when the NuGet package is not yet available.
// It sends an HTTP POST to the Copilot Studio Direct-to-Engine endpoint with
// the user's bearer token and returns the agent's text response.
// ===========================================================================
class CopilotStudioClientSimulator
{
    private readonly string _environmentId;
    private readonly string _schemaName;
    private readonly string _accessToken;
    private readonly HttpClient _http = new();
    private readonly string _baseUrl;
    private string _conversationId = Guid.NewGuid().ToString();

    public CopilotStudioClientSimulator(
        string connectionString,
        string environmentId,
        string schemaName,
        string accessToken)
    {
        _environmentId = environmentId;
        _schemaName = schemaName;
        _accessToken = accessToken;

        // Parse the endpoint from the connection string or fall back to the
        // standard Copilot Studio Direct-to-Engine URL pattern.
        _baseUrl = TryParseEndpoint(connectionString)
            ?? $"https://directline.botframework.com/v3/directline";
    }

    /// <summary>
    /// Sends a user message to the Copilot Studio agent and returns the
    /// agent's text reply.
    /// </summary>
    public async Task<string> SendActivityAsync(string prompt)
    {
        var payload = new
        {
            type = "message",
            from = new { id = "user" },
            text = prompt,
            conversationId = _conversationId
        };

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_baseUrl}/conversations/{_conversationId}/activities")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("text", out var textProp))
            return textProp.GetString() ?? "(empty response)";

        return body;
    }

    private static string? TryParseEndpoint(string connectionString)
    {
        // Connection strings from Copilot Studio typically contain
        // key-value pairs separated by semicolons, e.g.:
        //   Endpoint=https://...;EnvironmentId=...;AgentId=...
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 &&
                kv[0].Trim().Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
            {
                return kv[1].Trim();
            }
        }
        return null;
    }
}
