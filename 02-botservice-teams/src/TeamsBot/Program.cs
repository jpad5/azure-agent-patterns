using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using TeamsBot.Bots;
using TeamsBot.Dialogs;

var builder = WebApplication.CreateBuilder(args);

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

app.MapPost("/api/messages", async (HttpContext context, IBotFrameworkHttpAdapter adapter, IBot bot) =>
{
    await adapter.ProcessAsync(context.Request, context.Response, bot);
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
