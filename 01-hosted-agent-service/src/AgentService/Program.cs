using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// JWT Bearer authentication via Microsoft Identity Web with OBO support.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5010")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient("EnterpriseApi", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["EnterpriseApi:BaseUrl"]!);
});

builder.Services.AddHttpClient("CopilotStudio");

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/agent/invoke", async (
    AgentRequest request,
    ITokenAcquisition tokenAcquisition,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    // --- Step 1: Validate the incoming JWT (handled automatically by [Authorize]) ---
    var userName = httpContext.User.Identity?.Name ?? "unknown user";
    logger.LogInformation("Agent invoked by {User} with prompt: {Prompt}", userName, request.Prompt);

    // --- Step 2: Call Copilot Studio agent via conversations API ---
    var conversationsUrl = configuration["CopilotStudio:TokenEndpoint"]!;
    string agentResponse;
    try
    {
        // Get an OBO token for the Power Platform API to call the Copilot Studio bot
        var ppToken = await tokenAcquisition.GetAccessTokenForUserAsync(
            new[] { "https://api.powerplatform.com/CopilotStudio.Copilots.Invoke" });

        var csClient = httpClientFactory.CreateClient("CopilotStudio");
        csClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ppToken);

        // 2a. Start a conversation
        var startResp = await csClient.PostAsJsonAsync(conversationsUrl, new { });
        var startBody = await startResp.Content.ReadAsStringAsync();
        logger.LogInformation("Start conversation response ({StatusCode}): {Body}",
            (int)startResp.StatusCode, startBody);
        startResp.EnsureSuccessStatusCode();

        var convJson = JsonDocument.Parse(startBody).RootElement;
        var conversationId = convJson.GetProperty("conversationId").GetString()!;
        logger.LogInformation("Copilot Studio conversation started: {ConversationId}", conversationId);

        // 2b. Check if the start-conversation response already contains a bot message (greeting or error)
        var greetingResponse = ExtractBotResponse(startBody, logger);
        if (greetingResponse != "(no bot reply found)")
        {
            logger.LogInformation("Bot greeting/message received from start conversation: {Greeting}", greetingResponse);
            agentResponse = greetingResponse;
        }
        else
        {
            // 2c. Send user message — Copilot Studio: POST to /conversations/{id}
            var uriBuilder = new UriBuilder(conversationsUrl);
            uriBuilder.Path = uriBuilder.Path + "/" + conversationId;
            var turnUrl = uriBuilder.Uri.ToString();

            var turnPayload = new
            {
                activity = new
                {
                    type = "message",
                    text = request.Prompt
                }
            };
            var sendResp = await csClient.PostAsJsonAsync(turnUrl, turnPayload);
            var sendBody = await sendResp.Content.ReadAsStringAsync();
            logger.LogInformation("Execute turn response ({StatusCode}): {Body}",
                (int)sendResp.StatusCode, sendBody);
            sendResp.EnsureSuccessStatusCode();

            // 2d. Extract the bot's response from the reply
            agentResponse = ExtractBotResponse(sendBody, logger);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Copilot Studio call failed");
        return Results.Problem(detail: $"Copilot Studio call failed: {ex.Message}", statusCode: 502);
    }

    logger.LogInformation("Copilot Studio response: {Response}", agentResponse);

    // --- Step 3: OBO token exchange to call the Enterprise API as the user ---
    var enterpriseApiScope = configuration["EnterpriseApi:Scope"]!;
    string oboToken;
    try
    {
        oboToken = await tokenAcquisition.GetAccessTokenForUserAsync(
            new[] { enterpriseApiScope });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "OBO token acquisition failed");
        return Results.Problem(
            detail: "Failed to acquire token for Enterprise API via OBO.",
            statusCode: 502);
    }

    // --- Step 4: Call the Enterprise API with the OBO token ---
    var client = httpClientFactory.CreateClient("EnterpriseApi");
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", oboToken);

    object? enterpriseData;
    try
    {
        var apiResponse = await client.GetAsync("/api/me");
        apiResponse.EnsureSuccessStatusCode();
        enterpriseData = await apiResponse.Content.ReadFromJsonAsync<object>();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Enterprise API call failed — returning simulated data");
        enterpriseData = new { note = "Enterprise API unavailable; simulated data", user = userName };
    }

    // --- Step 5: Return combined response ---
    return Results.Ok(new
    {
        agentResponse,
        enterpriseData,
        metadata = new
        {
            pattern = "01-HostedAgentService",
            flow = "Frontend SSO → JWT validation → Copilot Studio (conversations API) → OBO → Enterprise API"
        }
    });
})
.RequireAuthorization();

app.Run();

// ---------------------------------------------------------------------------
// Extract the bot's text response from the Copilot Studio activities reply.
// ---------------------------------------------------------------------------
static string ExtractBotResponse(string responseBody, ILogger logger)
{
    try
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        // The response may contain an "activities" array with bot messages
        if (root.TryGetProperty("activities", out var activities))
        {
            foreach (var act in activities.EnumerateArray())
            {
                var type = act.TryGetProperty("type", out var tp) ? tp.GetString() : null;
                if (type == "message" &&
                    act.TryGetProperty("from", out var from) &&
                    from.TryGetProperty("role", out var role) &&
                    role.GetString() == "bot")
                {
                    if (act.TryGetProperty("text", out var text))
                        return text.GetString() ?? "(empty response)";
                }
            }
        }

        // Single activity response
        if (root.TryGetProperty("text", out var directText))
            return directText.GetString() ?? "(empty response)";

        return "(no bot reply found)";
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to parse bot response");
        return responseBody;
    }
}

public record AgentRequest(string Prompt);
