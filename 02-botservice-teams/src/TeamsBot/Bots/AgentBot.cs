using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using TeamsBot.Dialogs;

namespace TeamsBot.Bots;

/// <summary>
/// Teams-aware bot that runs MainDialog for every message and greets new members.
/// </summary>
public class AgentBot : DialogBot<MainDialog>
{
    public AgentBot(
        ConversationState conversationState,
        UserState userState,
        MainDialog dialog,
        ILogger<AgentBot> logger)
        : base(conversationState, userState, dialog, logger)
    {
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
                        "👋 Welcome to **Agent Bot – Teams Pattern**!\n\n" +
                        "This bot demonstrates the Bot Framework v4 OAuth Prompt flow, " +
                        "Copilot Studio orchestration simulation, and On-Behalf-Of (OBO) " +
                        "token exchange to call an Enterprise API.\n\n" +
                        "Type anything to get started — you'll be prompted to sign in first."),
                    cancellationToken);
            }
        }
    }
}
