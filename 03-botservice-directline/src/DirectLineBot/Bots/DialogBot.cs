using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;

namespace DirectLineBot.Bots;

/// <summary>
/// Base bot that runs a dialog on every incoming message and persists state.
/// </summary>
public class DialogBot<T> : ActivityHandler where T : Dialog
{
    protected readonly Dialog Dialog;
    protected readonly ConversationState ConversationState;
    protected readonly UserState UserState;
    protected readonly ILogger Logger;

    public DialogBot(
        ConversationState conversationState,
        UserState userState,
        T dialog,
        ILogger<DialogBot<T>> logger)
    {
        ConversationState = conversationState;
        UserState = userState;
        Dialog = dialog;
        Logger = logger;
    }

    public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default)
    {
        await base.OnTurnAsync(turnContext, cancellationToken);

        // Persist any state changes at the end of the turn.
        await ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
        await UserState.SaveChangesAsync(turnContext, false, cancellationToken);
    }

    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Running dialog from message activity.");
        await Dialog.RunAsync(
            turnContext,
            ConversationState.CreateProperty<DialogState>("DialogState"),
            cancellationToken);
    }
}
