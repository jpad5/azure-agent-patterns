using System.Net.Http.Headers;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Identity.Client;

namespace DirectLineBot.Dialogs;

/// <summary>
/// Waterfall dialog that checks for a backchannel user token first (Option A),
/// falls back to OAuthPrompt (Option B), then performs OBO → Enterprise API call.
/// </summary>
public class MainDialog : ComponentDialog
{
    private const string BackchannelTokenKey = "BackchannelUserToken";

    private readonly IConfiguration _config;
    private readonly ILogger<MainDialog> _logger;

    public MainDialog(IConfiguration configuration, ILogger<MainDialog> logger)
        : base(nameof(MainDialog))
    {
        _config = configuration;
        _logger = logger;

        AddDialog(new OAuthPrompt(
            nameof(OAuthPrompt),
            new OAuthPromptSettings
            {
                ConnectionName = _config["ConnectionName"],
                Text = "Please sign in to continue.",
                Title = "Sign In",
                Timeout = 300000 // 5 minutes
            }));

        AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
        {
            CheckBackchannelTokenStepAsync,
            OAuthStepAsync,
            ProcessStepAsync
        }));

        InitialDialogId = nameof(WaterfallDialog);
    }

    /// <summary>
    /// Step 1: Check if a user token was delivered via backchannel.
    /// If found, skip the OAuth prompt and go straight to processing.
    /// </summary>
    private async Task<DialogTurnResult> CheckBackchannelTokenStepAsync(
        WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        var conversationState = stepContext.Context.TurnState.Get<ConversationState>();
        var accessor = conversationState.CreateProperty<string>(BackchannelTokenKey);
        var backchannelToken = await accessor.GetAsync(stepContext.Context, () => null!, cancellationToken);

        if (!string.IsNullOrEmpty(backchannelToken))
        {
            _logger.LogInformation("Backchannel token found — skipping OAuthPrompt.");
            // Pass the token forward; skip the OAuthPrompt step.
            stepContext.Values["UserToken"] = backchannelToken;
            return await stepContext.NextAsync(null, cancellationToken);
        }

        _logger.LogInformation("No backchannel token — proceeding to OAuthPrompt.");
        return await stepContext.NextAsync(null, cancellationToken);
    }

    /// <summary>
    /// Step 2: If no backchannel token, use OAuthPrompt as fallback.
    /// </summary>
    private async Task<DialogTurnResult> OAuthStepAsync(
        WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        // If we already have a token from backchannel, skip this step.
        if (stepContext.Values.ContainsKey("UserToken"))
        {
            return await stepContext.NextAsync(null, cancellationToken);
        }

        return await stepContext.BeginDialogAsync(nameof(OAuthPrompt), null, cancellationToken);
    }

    /// <summary>
    /// Step 3: Perform OBO token exchange and call the Enterprise API.
    /// </summary>
    private async Task<DialogTurnResult> ProcessStepAsync(
        WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
    {
        // Resolve the user token from backchannel or OAuthPrompt.
        string? userToken = null;

        if (stepContext.Values.ContainsKey("UserToken"))
        {
            userToken = (string)stepContext.Values["UserToken"];
            _logger.LogInformation("Using backchannel token for OBO exchange.");
        }
        else if (stepContext.Result is TokenResponse tokenResponse)
        {
            userToken = tokenResponse.Token;
            _logger.LogInformation("Using OAuthPrompt token for OBO exchange.");
        }

        if (string.IsNullOrEmpty(userToken))
        {
            await stepContext.Context.SendActivityAsync(
                MessageFactory.Text("❌ Sign-in failed. Please try again."),
                cancellationToken);
            return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken);
        }

        var userQuery = stepContext.Context.Activity.Text ?? "(no message)";

        // Simulate Copilot Studio orchestration
        _logger.LogInformation("Simulating Copilot Studio orchestration for query: {Query}", userQuery);
        var agentResponse = $"[Simulated Agent Response] Processed: \"{userQuery}\"";

        // --- OBO token exchange to call the Enterprise API ---
        string enterpriseData;
        try
        {
            var clientId = _config["MicrosoftAppId"];
            var clientSecret = _config["MicrosoftAppPassword"];
            var tenantId = _config["MicrosoftAppTenantId"];
            var enterpriseApiScope = _config["EnterpriseApi:Scope"]!;

            var app = ConfidentialClientApplicationBuilder.Create(clientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .Build();

            var oboResult = await app.AcquireTokenOnBehalfOf(
                    new[] { enterpriseApiScope },
                    new UserAssertion(userToken))
                .ExecuteAsync(cancellationToken);

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", oboResult.AccessToken);

            var baseUrl = _config["EnterpriseApi:BaseUrl"] ?? "http://localhost:5050";
            enterpriseData = await httpClient.GetStringAsync($"{baseUrl}/api/me", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Enterprise API call failed; using fallback data.");
            enterpriseData = $"{{\"error\": \"{ex.Message}\"}}";
        }

        // --- Adaptive Card response ---
        var cardJson = BuildAdaptiveCard(userQuery, agentResponse, enterpriseData);
        var cardAttachment = new Attachment
        {
            ContentType = "application/vnd.microsoft.card.adaptive",
            Content = Newtonsoft.Json.JsonConvert.DeserializeObject(cardJson)
        };

        var reply = MessageFactory.Attachment(cardAttachment);
        await stepContext.Context.SendActivityAsync(reply, cancellationToken);

        // Loop back for the next conversation turn.
        return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken);
    }

    private static string BuildAdaptiveCard(string userQuery, string agentResponse, string enterpriseData)
    {
        return $$"""
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.4",
          "body": [
            {
              "type": "TextBlock",
              "size": "Medium",
              "weight": "Bolder",
              "text": "Agent Bot Response"
            },
            {
              "type": "FactSet",
              "facts": [
                { "title": "Your Query", "value": "{{userQuery}}" },
                { "title": "Agent Response", "value": "{{agentResponse}}" },
                { "title": "Enterprise API Data", "value": "{{enterpriseData.Replace("\"", "\\\"").Replace("\n", " ")}}" }
              ]
            }
          ]
        }
        """;
    }
}
