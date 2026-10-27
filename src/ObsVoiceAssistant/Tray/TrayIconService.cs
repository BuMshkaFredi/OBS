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
    private readonly Forms.ToolStripMenuItem overlayItem;
    private readonly Action configureAction;
    private readonly Action exitAction;
    private readonly Action<bool>? overlayAction;

    public TrayIconService(
        Action configureAction,
        Action exitAction,
        Action<bool>? overlayAction = null)
    {
        this.configureAction = configureAction;
        this.exitAction = exitAction;
        this.overlayAction = overlayAction;
        statusItem = new Forms.ToolStripMenuItem("OBS: подключение...") { Enabled = false };
        overlayItem = new Forms.ToolStripMenuItem("Счётчик зрителей") { CheckOnClick = true };
        overlayItem.Click += (_, _) => this.overlayAction?.Invoke(overlayItem.Checked);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add("Настройки", null, (_, _) => this.configureAction());
        menu.Items.Add(overlayItem);
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

    /// <summary>Синхронизирует галочку с настройкой после окна настроек или запуска.</summary>
    public void SetOverlayEnabled(bool enabled) => OnUi(() => overlayItem.Checked = enabled);

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
