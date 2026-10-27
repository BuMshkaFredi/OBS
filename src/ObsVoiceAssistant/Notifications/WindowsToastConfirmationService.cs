using CommunityToolkit.WinUI.Notifications;
using ObsVoiceAssistant.Commands;
using Windows.UI.Notifications;

namespace ObsVoiceAssistant.Notifications;

public sealed class WindowsToastConfirmationService : IConfirmationService
{
    public Task<bool> ConfirmAsync(ParsedCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestId = Guid.NewGuid().ToString("N");

        var toast = new ToastContentBuilder()
            .AddText("\u041f\u043e\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0435\u043d\u0438\u0435 \u043a\u043e\u043c\u0430\u043d\u0434\u044b")
            .AddText(GetDescription(command))
            .AddButton(new ToastButton("\u041f\u043e\u0434\u0442\u0432\u0435\u0440\u0434\u0438\u0442\u044c", $"confirm={requestId}").SetBackgroundActivation())
            .AddButton(new ToastButton("\u041e\u0442\u043c\u0435\u043d\u0430", $"cancel={requestId}").SetBackgroundActivation())
            .GetToastContent();

        ToastNotificationManagerCompat.OnActivated += args =>
        {
            if (!args.Argument.Contains(requestId, StringComparison.Ordinal))
            {
                return;
            }

            result.TrySetResult(args.Argument.StartsWith("confirm=", StringComparison.Ordinal));
        };

        var notification = new ToastNotification(toast.GetXml()) { Tag = requestId, Group = "dangerous-commands" };
        notification.Dismissed += (_, _) => result.TrySetResult(false);
        notification.Failed += (_, _) => result.TrySetResult(false);
        ToastNotificationManagerCompat.CreateToastNotifier().Show(notification);

        return AwaitResultAsync(result.Task, cancellationToken);
    }

    private static async Task<bool> AwaitResultAsync(Task<bool> result, CancellationToken cancellationToken)
    {
        var cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var completed = await Task.WhenAny(result, cancellation).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return completed == result && await result.ConfigureAwait(false);
    }

    private static string GetDescription(ParsedCommand command) => command.Intent switch
    {
        CommandIntent.StopRecording => "\u041e\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u044c \u0437\u0430\u043f\u0438\u0441\u044c \u0432 OBS?",
        CommandIntent.StopStream => "\u041e\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u044c \u0442\u0440\u0430\u043d\u0441\u043b\u044f\u0446\u0438\u044e \u0432 OBS?",
        CommandIntent.SetStreamTitle => $"\u0418\u0437\u043c\u0435\u043d\u0438\u0442\u044c \u043d\u0430\u0437\u0432\u0430\u043d\u0438\u0435 \u0442\u0440\u0430\u043d\u0441\u043b\u044f\u0446\u0438\u0438 \u043d\u0430 \u00ab{command.Argument}\u00bb?",
        CommandIntent.SetStreamCategory => $"\u0418\u0437\u043c\u0435\u043d\u0438\u0442\u044c \u043a\u0430\u0442\u0435\u0433\u043e\u0440\u0438\u044e \u0442\u0440\u0430\u043d\u0441\u043b\u044f\u0446\u0438\u0438 \u043d\u0430 \u00ab{command.Argument}\u00bb (\u043d\u0430\u0439\u0434\u0435\u043d\u043e \u043d\u0430 Twitch)?",
        _ => "\u0412\u044b\u043f\u043e\u043b\u043d\u0438\u0442\u044c \u043e\u043f\u0430\u0441\u043d\u0443\u044e \u043a\u043e\u043c\u0430\u043d\u0434\u0443?"
    };
}
