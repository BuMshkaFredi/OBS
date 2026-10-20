using System.Drawing;
using Forms = System.Windows.Forms;
using ObsVoiceAssistant.Configuration;

namespace ObsVoiceAssistant.Tray;

public sealed class TrayIconService : ITrayStatus, IDisposable
{
    private const string Title = "OBS Voice Assistant";

    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem statusItem;
    private readonly Action configureAction;
    private readonly Action exitAction;

    public TrayIconService(Action configureAction, Action exitAction)
    {
        this.configureAction = configureAction;
        this.exitAction = exitAction;
        statusItem = new Forms.ToolStripMenuItem("OBS: подключение...") { Enabled = false };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add("Настроить OBS", null, (_, _) => this.configureAction());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => this.exitAction());

        icon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = Title,
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    public void SetStatus(string status) => OnUi(() =>
    {
        statusItem.Text = $"OBS: {status}";
        icon.Text = $"{Title} — {status}";
    });

    /// <summary>
    /// Показывает всплывающее уведомление трея. Нужен для случаев, когда команда
    /// распознана, но не выполнена: иначе пользователь повторяет фразу вслепую.
    /// </summary>
    public void Notify(string message) =>
        OnUi(() => icon.ShowBalloonTip(5000, Title, message, Forms.ToolTipIcon.Warning));

    /// <summary>Запись в трей приходит из фоновых потоков, а WinForms требует UI-поток.</summary>
    private void OnUi(Action action)
    {
        var owner = icon.ContextMenuStrip;
        if (owner is not null && owner.InvokeRequired)
        {
            owner.BeginInvoke(action);
            return;
        }

        action();
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
