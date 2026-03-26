using System.Net.Http.Headers;
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

    // --- Step 2: Simulate calling Copilot Studio via M365 Agents SDK ---
    // In a real implementation, you would use the M365 Agents SDK here:
    //   var agentClient = new AgentsClient(connectionSettings);
    //   var activity = MessageFactory.Text(request.Prompt);
    //   var response = await agentClient.GetResponseAsync(activity);
    // The SDK would route the prompt to a Copilot Studio agent and return its response.
    var agentResponse = $"[Copilot Studio] Processed prompt: \"{request.Prompt}\" — " +
                        "this is a simulated response. The M365 Agents SDK would orchestrate the real call.";
    logger.LogInformation("Simulated Copilot Studio response generated");

    // --- Step 3: Simulate a tool invocation that requires Enterprise API data ---
    // The agent determines it needs enterprise data to fulfill the request.
    // Perform On-Behalf-Of (OBO) token exchange to call the Enterprise API as the user.
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
            flow = "Frontend SSO → JWT validation → Copilot Studio (simulated) → OBO → Enterprise API"
        }
    });
})
.RequireAuthorization();

app.Run();

public record AgentRequest(string Prompt);
