using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ObsVoiceAssistant.Notifications;

/// <summary>
/// Панель уведомлений в правом нижнем углу. Заменяет баллон трея, чтобы не было
/// предупреждающего значка и размер задавало приложение. Компактный режим — для
/// коротких сообщений о потерянной команде, обычный — для остальных результатов.
/// </summary>
public partial class NotificationPanelWindow : Window
{
    private static readonly TimeSpan CompactDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan NormalDuration = TimeSpan.FromSeconds(4);

    private readonly bool compact;
    private readonly DispatcherTimer hideTimer;

    public NotificationPanelWindow(bool compact)
    {
        InitializeComponent();
        this.compact = compact;
        ApplyLayout();
        hideTimer = new DispatcherTimer
        {
            Interval = compact ? CompactDuration : NormalDuration
        };
        hideTimer.Tick += (_, _) => Hide();
    }

    public void ShowMessage(string message)
    {
        MessageText.Text = message;

        if (!IsVisible)
        {
            // Показываем прозрачным на время измерения, чтобы панель не мигнула в левом верхнем углу.
            Opacity = 0;
            Show();
        }

        UpdateLayout();
        PositionNearTray();
        Opacity = 1;

        // Новое сообщение заменяет предыдущее и продлевает показ.
        hideTimer.Stop();
        hideTimer.Start();
    }

    private void ApplyLayout()
    {
        if (compact)
        {
            Width = 260;
            Height = 44;
            MinHeight = 0;
            SizeToContent = SizeToContent.Manual;
            NotificationBorder.Padding = new Thickness(12, 6, 12, 6);
            MessageText.FontSize = 12;
            MessageText.TextWrapping = TextWrapping.NoWrap;
            MessageText.TextTrimming = TextTrimming.CharacterEllipsis;
            return;
        }

        Width = 380;
        Height = double.NaN;
        MinHeight = 56;
        SizeToContent = SizeToContent.Height;
        NotificationBorder.Padding = new Thickness(16, 10, 16, 10);
        MessageText.FontSize = 13;
        MessageText.TextWrapping = TextWrapping.Wrap;
        MessageText.TextTrimming = TextTrimming.None;
    }

    private void PositionNearTray()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 12;
        Top = workArea.Bottom - ActualHeight - 12;
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Hide();
}
