using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

// ---------------------------------------------------------------------------
// Pattern 4 — Action Endpoint
//
// ASP.NET Core minimal API that Copilot Studio invokes as a custom action.
// It performs OBO token exchange and calls the downstream Enterprise API.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// ── Health check ───────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "healthy", component = "ActionEndpoint" }));

// ── Custom action: get-employee-profile ────────────────────────────────
app.MapPost("/api/actions/get-employee-profile", async (HttpContext ctx, IConfiguration config) =>
{
    // 1. Extract the user assertion token from the incoming request.
    //    Copilot Studio forwards the user's token when invoking the action.
    var authHeader = ctx.Request.Headers.Authorization.ToString();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var userToken = authHeader["Bearer ".Length..];

    // 2. OBO token exchange — acquire a token for the Enterprise API on
    //    behalf of the calling user.
    var confidentialApp = ConfidentialClientApplicationBuilder
        .Create(config["AzureAd:ClientId"])
        .WithClientSecret(config["AzureAd:ClientSecret"])
        .WithAuthority($"{config["AzureAd:Instance"]}{config["AzureAd:TenantId"]}")
        .Build();

    AuthenticationResult oboResult;
    try
    {
        oboResult = await confidentialApp
            .AcquireTokenOnBehalfOf(
                new[] { config["EnterpriseApi:Scope"]! },
                new UserAssertion(userToken))
            .ExecuteAsync();
    }
    catch (MsalServiceException ex)
    {
        return Results.Problem(
            detail: $"OBO token exchange failed: {ex.Message}",
            statusCode: StatusCodes.Status502BadGateway);
    }

    // 3. Call the downstream Enterprise API with the OBO token.
    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", oboResult.AccessToken);

    var apiBaseUrl = config["EnterpriseApi:BaseUrl"] ?? "http://localhost:5050";

    HttpResponseMessage apiResponse;
    try
    {
        apiResponse = await httpClient.GetAsync($"{apiBaseUrl}/api/me");
        apiResponse.EnsureSuccessStatusCode();
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            detail: $"Enterprise API call failed: {ex.Message}",
            statusCode: StatusCodes.Status502BadGateway);
    }

    var apiBody = await apiResponse.Content.ReadAsStringAsync();

    // 4. Return in the format Copilot Studio expects for action responses.
    return Results.Ok(new
    {
        status = "success",
        data = JsonSerializer.Deserialize<JsonElement>(apiBody)
    });

}).RequireAuthorization();

app.Run();
