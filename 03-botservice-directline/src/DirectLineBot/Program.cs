using System.Text;
using System.Text.Json;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using DirectLineBot.Bots;
using DirectLineBot.Dialogs;

var builder = WebApplication.CreateBuilder(args);

// CORS for the web client
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Bot Framework authentication
builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

// Bot adapter with error handler
builder.Services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();

// State management backed by in-memory storage
var storage = new MemoryStorage();
builder.Services.AddSingleton<IStorage>(storage);
builder.Services.AddSingleton(new UserState(storage));
builder.Services.AddSingleton(new ConversationState(storage));

// Dialogs
builder.Services.AddSingleton<MainDialog>();

// The bot itself
builder.Services.AddTransient<IBot, AgentBot>();

var app = builder.Build();

app.UseCors();

// Bot Framework messages endpoint
app.MapPost("/api/messages", async (HttpContext context, IBotFrameworkHttpAdapter adapter, IBot bot) =>
{
    await adapter.ProcessAsync(context.Request, context.Response, bot);
});

// Direct Line token exchange endpoint
app.MapPost("/api/directline/token", async (IConfiguration config, ILogger<Program> logger) =>
{
    var secret = config["DirectLineSecret"];
    if (string.IsNullOrEmpty(secret))
    {
        logger.LogError("DirectLineSecret is not configured.");
        return Results.Problem("DirectLineSecret is not configured.", statusCode: 500);
    }

    try
    {
        using var httpClient = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post,
            "https://directline.botframework.com/v3/directline/tokens/generate");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var token = json.RootElement.GetProperty("token").GetString();
        var conversationId = json.RootElement.TryGetProperty("conversationId", out var cid)
            ? cid.GetString() : null;

        logger.LogInformation("Direct Line token generated successfully.");

        return Results.Json(new { token, conversationId });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to generate Direct Line token.");
        return Results.Problem($"Token generation failed: {ex.Message}", statusCode: 500);
    }
});

app.Run();

/// <summary>
/// CloudAdapter with a global error handler that sends a trace and message on failure.
/// </summary>
public class AdapterWithErrorHandler : CloudAdapter
{
    public AdapterWithErrorHandler(
        BotFrameworkAuthentication auth,
        ILogger<AdapterWithErrorHandler> logger)
        : base(auth, logger)
    {
        OnTurnError = async (turnContext, exception) =>
        {
            logger.LogError(exception, "[OnTurnError] unhandled error: {Message}", exception.Message);
            await turnContext.SendActivityAsync("Sorry, something went wrong. Please try again.");
        };
    }
}
