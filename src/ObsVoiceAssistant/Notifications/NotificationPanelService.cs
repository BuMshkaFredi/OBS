namespace ObsVoiceAssistant.Notifications;

/// <summary>
/// Показывает панель уведомлений на UI-потоке. Сообщения приходят из фонового
/// распознавания и OBS-событий, а окно можно создавать только в диспетчере.
/// Для двух размеров держим отдельные окна, чтобы переключение не оставляло
/// старую высоту и панель не прыгала.
/// </summary>
public sealed class NotificationPanelService : IDisposable
{
    private NotificationPanelWindow? compactWindow;
    private NotificationPanelWindow? normalWindow;

    /// <summary>Обычное уведомление: крупнее компактного, длинный текст переносится.</summary>
    public void Show(string message) => Post(() =>
    {
        HideIfVisible(compactWindow);
        normalWindow ??= new NotificationPanelWindow(compact: false);
        normalWindow.ShowMessage(message);
    });

    /// <summary>Короткое сообщение о потерянной команде.</summary>
    public void ShowCompact(string message) => Post(() =>
    {
        HideIfVisible(normalWindow);
        compactWindow ??= new NotificationPanelWindow(compact: true);
        compactWindow.ShowMessage(message);
    });

    private static void HideIfVisible(NotificationPanelWindow? window)
    {
        if (window?.IsVisible == true)
        {
            window.Hide();
        }
    }

    private static void Post(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        dispatcher.InvokeAsync(action);
    }

    public void Dispose()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        dispatcher.InvokeAsync(() =>
        {
            compactWindow?.Close();
            normalWindow?.Close();
            compactWindow = null;
            normalWindow = null;
        });
    }
}
