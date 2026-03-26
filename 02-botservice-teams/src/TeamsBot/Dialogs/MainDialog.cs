using System.Net.Http.Headers;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Identity.Client;

namespace TeamsBot.Dialogs;

/// <summary>
/// Waterfall dialog: OAuth sign-in → simulate Copilot Studio → OBO → Enterprise API → Adaptive Card.
/// </summary>
public class MainDialog : ComponentDialog
{
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
            OAuthStepAsync,
            ProcessStepAsync
        }));

        InitialDialogId = nameof(WaterfallDialog);
    }

    private async Task<DialogTurnResult> OAuthStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        return await stepContext.BeginDialogAsync(nameof(OAuthPrompt), null, cancellationToken);
    }

    private async Task<DialogTurnResult> ProcessStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
    {
        var tokenResponse = (TokenResponse)stepContext.Result;
        if (tokenResponse?.Token == null)
        {
            await stepContext.Context.SendActivityAsync(
                MessageFactory.Text("Sign-in failed. Please try again."), cancellationToken);
            return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken);
        }

        var userToken = tokenResponse.Token;
        var userQuery = stepContext.Context.Activity.Text ?? "(no message)";

        // In production, forward prompt to Copilot Studio here.
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
