using Microsoft.Agents.CopilotStudio.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

// ---------------------------------------------------------------------------
// Pattern 4 — Agents SDK Direct-to-Engine (with Third-Party IDP)
//
// This console app authenticates the user via MSAL and talks directly to a
// Copilot Studio agent using the CopilotClient from the M365 Agents SDK.
// The SDK handles SSE streaming and conversation lifecycle management.
// ---------------------------------------------------------------------------

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var tenantId = config["AzureAd:TenantId"]!;
var clientId = config["AzureAd:ClientId"]!;

// ── 1. Authenticate the user via MSAL interactive sign-in ──────────────
// For federated third-party IDP (Okta, Ping, Auth0): configure Entra ID
// federation. MSAL will automatically redirect to the federated IDP during
// interactive sign-in.

var msalApp = PublicClientApplicationBuilder.Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithRedirectUri("http://localhost")
    .Build();

var scopes = new[] { "https://api.powerplatform.com/CopilotStudio.Copilots.Invoke" };

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

// ── 2. Initialize the CopilotClient from the Agents SDK ───────────────

var connectionSettings = new ConnectionSettings
{
    EnvironmentId = config["CopilotStudio:EnvironmentId"]!,
    SchemaName = config["CopilotStudio:SchemaName"]!,
};

// Token provider function — MSAL acquires/refreshes the token as needed.
async Task<string> GetTokenAsync(string _)
{
    try
    {
        var result = await msalApp
            .AcquireTokenSilent(scopes, authResult.Account)
            .ExecuteAsync();
        return result.AccessToken;
    }
    catch (MsalUiRequiredException)
    {
        var result = await msalApp
            .AcquireTokenInteractive(scopes)
            .ExecuteAsync();
        return result.AccessToken;
    }
}

var services = new ServiceCollection();
services.AddHttpClient("mcs");
services.AddLogging(b => b.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
var sp = services.BuildServiceProvider();

var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
var logger = sp.GetRequiredService<ILogger<CopilotClient>>();

var copilotClient = new CopilotClient(
    connectionSettings,
    httpClientFactory,
    GetTokenAsync,
    logger,
    "mcs");

// ── 3. Interactive chat loop ───────────────────────────────────────────
// StartConversationAsync returns an IAsyncEnumerable<Activity> that handles
// SSE streaming from the Direct-to-Engine endpoint.

Console.WriteLine("Starting conversation with agent...");
Console.WriteLine();

// Start the conversation and process the greeting (if any).
await foreach (var activity in copilotClient.StartConversationAsync(
    emitStartConversationEvent: true))
{
    if (activity.Type == "message" && !string.IsNullOrEmpty(activity.Text))
        Console.WriteLine($"Agent: {activity.Text}");
}

Console.WriteLine();
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
        await foreach (var activity in copilotClient.AskQuestionAsync(input))
        {
            if (activity.Type == "message" && !string.IsNullOrEmpty(activity.Text))
                Console.WriteLine($"Agent: {activity.Text}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
    }

    Console.WriteLine();
}

Console.WriteLine("Session ended.");
