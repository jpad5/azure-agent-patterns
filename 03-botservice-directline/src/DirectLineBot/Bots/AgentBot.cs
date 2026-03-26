using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using DirectLineBot.Dialogs;

namespace DirectLineBot.Bots;

/// <summary>
/// Direct Line bot that handles backchannel events for token delivery
/// and falls back to OAuthPrompt when no backchannel token is available.
/// </summary>
public class AgentBot : DialogBot<MainDialog>
{
    private const string BackchannelTokenKey = "BackchannelUserToken";

    public AgentBot(
        ConversationState conversationState,
        UserState userState,
        MainDialog dialog,
        ILogger<AgentBot> logger)
        : base(conversationState, userState, dialog, logger)
    {
    }

    protected override async Task OnEventActivityAsync(
        ITurnContext<IEventActivity> turnContext,
        CancellationToken cancellationToken)
    {
        // Handle backchannel token delivery from the Web Chat client.
        if (string.Equals(turnContext.Activity.Name, "userToken", StringComparison.OrdinalIgnoreCase))
        {
            var token = turnContext.Activity.Value?.ToString();
            if (!string.IsNullOrEmpty(token))
            {
                // Store the user token in conversation state so MainDialog can use it.
                var accessor = ConversationState.CreateProperty<string>(BackchannelTokenKey);
                await accessor.SetAsync(turnContext, token, cancellationToken);
                await ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);

                Logger.LogInformation("Backchannel user token received and stored in conversation state.");
                await turnContext.SendActivityAsync(
                    MessageFactory.Text("✅ Your identity token was received via backchannel. You can now send messages without a separate sign-in prompt."),
                    cancellationToken);
            }
            else
            {
                Logger.LogWarning("Backchannel userToken event received but value was empty.");
            }

            return;
        }

        await base.OnEventActivityAsync(turnContext, cancellationToken);
    }

    protected override async Task OnMembersAddedAsync(
        IList<ChannelAccount> membersAdded,
        ITurnContext<IConversationUpdateActivity> turnContext,
        CancellationToken cancellationToken)
    {
        foreach (var member in membersAdded)
        {
            if (member.Id != turnContext.Activity.Recipient.Id)
            {
                await turnContext.SendActivityAsync(
                    MessageFactory.Text(
                        "👋 Welcome to **Agent Bot – Direct Line Pattern**!\n\n" +
                        "This bot demonstrates two authentication options:\n" +
                        "- **Option A** — Send your token via backchannel (no sign-in prompt)\n" +
                        "- **Option B** — OAuth Prompt fallback (you'll be asked to sign in)\n\n" +
                        "Type anything to get started."),
                    cancellationToken);
            }
        }
    }
}
