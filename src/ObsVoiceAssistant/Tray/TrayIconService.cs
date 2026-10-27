using System.Drawing;
using Forms = System.Windows.Forms;
using ObsVoiceAssistant.Configuration;

namespace ObsVoiceAssistant.Tray;

public sealed class TrayIconService : ITrayStatus, IDisposable
{
    private const string Title = "OBS Voice Assistant";

    private readonly Forms.NotifyIcon icon;
    private readonly Icon appIcon;
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
        menu.Items.Add("Настройки", null, (_, _) => this.configureAction());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => this.exitAction());

        appIcon = LoadAppIcon();
        icon = new Forms.NotifyIcon
        {
            Icon = appIcon,
            Text = Title,
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    private static Icon LoadAppIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        if (resource is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var stream = resource.Stream;
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }

    public void SetStatus(string status) => OnUi(() =>
    {
        statusItem.Text = $"OBS: {status}";
        icon.Text = $"{Title} — {status}";
    });

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
        appIcon.Dispose();
    }
}
