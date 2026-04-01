using System.Net.Http.Headers;
using Microsoft.Agents.CopilotStudio.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// File logging — writes to Logs/agentservice-YYYYMMDD.txt with daily rolling.
builder.Logging.AddFile("Logs/agentservice-{Date}.txt");

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

// Named HttpClient for the CopilotClient SDK.
builder.Services.AddHttpClient("CopilotStudio");

// Register ConnectionSettings for the CopilotClient SDK.
builder.Services.AddSingleton(_ =>
{
    var cs = builder.Configuration.GetSection("CopilotStudio");
    return new ConnectionSettings
    {
        EnvironmentId = cs["EnvironmentId"]!,
        SchemaName = cs["SchemaName"]!,
    };
});

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/agent/invoke", async (
    AgentRequest request,
    ITokenAcquisition tokenAcquisition,
    IHttpClientFactory httpClientFactory,
    ConnectionSettings connectionSettings,
    IConfiguration configuration,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    // --- Step 1: Validate the incoming JWT (handled automatically by [Authorize]) ---
    var userName = httpContext.User.Identity?.Name ?? "unknown user";
    var claims = httpContext.User.Claims.Select(c => $"{c.Type}={c.Value}").ToList();
    logger.LogInformation("Agent invoked by {User} with prompt: {Prompt}", userName, request.Prompt);
    logger.LogDebug("Authenticated user claims: {Claims}", string.Join("; ", claims));

    // --- Step 2: Call Copilot Studio agent via CopilotClient SDK ---
    string agentResponse;
    try
    {
        // Token provider — acquires an OBO token for the Power Platform API on each call.
        async Task<string> GetCopilotTokenAsync(string _)
        {
            logger.LogDebug("Requesting OBO token for Power Platform API scope: CopilotStudio.Copilots.Invoke");
            var token = await tokenAcquisition.GetAccessTokenForUserAsync(
                new[] { "https://api.powerplatform.com/CopilotStudio.Copilots.Invoke" });
            logger.LogDebug("Power Platform OBO token acquired (length={TokenLength})", token.Length);
            return token;
        }

        var copilotClient = new CopilotClient(
            connectionSettings,
            httpClientFactory,
            GetCopilotTokenAsync,
            logger,
            "CopilotStudio");

        // Start a conversation and collect the greeting (if any).
        var responses = new List<string>();
        await foreach (var activity in copilotClient.StartConversationAsync(
            emitStartConversationEvent: true))
        {
            if (activity.Type == "message" && !string.IsNullOrEmpty(activity.Text))
            {
                logger.LogInformation("Bot greeting: {Text}", activity.Text);
                responses.Add(activity.Text);
            }
        }

        // Send the user's prompt and collect the agent's reply.
        await foreach (var activity in copilotClient.AskQuestionAsync(request.Prompt))
        {
            if (activity.Type == "message" && !string.IsNullOrEmpty(activity.Text))
            {
                logger.LogInformation("Bot reply: {Text}", activity.Text);
                responses.Add(activity.Text);
            }
        }

        agentResponse = responses.Count > 0
            ? string.Join("\n", responses)
            : "(no bot reply found)";
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Copilot Studio call failed");
        return Results.Problem(detail: $"Copilot Studio call failed: {ex.Message}", statusCode: 502);
    }

    logger.LogInformation("Copilot Studio response: {Response}", agentResponse);

    // --- Step 3: OBO token exchange to call the Enterprise API as the user ---
    var enterpriseApiScope = configuration["EnterpriseApi:Scope"]!;
    logger.LogDebug("Requesting OBO token for Enterprise API scope: {Scope}", enterpriseApiScope);
    string oboToken;
    try
    {
        oboToken = await tokenAcquisition.GetAccessTokenForUserAsync(
            new[] { enterpriseApiScope });
        logger.LogDebug("Enterprise API OBO token acquired (length={TokenLength})", oboToken.Length);
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

    var enterpriseBaseUrl = configuration["EnterpriseApi:BaseUrl"];
    logger.LogDebug("Calling Enterprise API at {BaseUrl}/api/me", enterpriseBaseUrl);
    object? enterpriseData;
    try
    {
        var apiResponse = await client.GetAsync("/api/me");
        logger.LogDebug("Enterprise API response: {StatusCode}", (int)apiResponse.StatusCode);
        apiResponse.EnsureSuccessStatusCode();
        enterpriseData = await apiResponse.Content.ReadFromJsonAsync<object>();
        logger.LogDebug("Enterprise API data received: {Data}", enterpriseData);
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

public record AgentRequest(string Prompt);
