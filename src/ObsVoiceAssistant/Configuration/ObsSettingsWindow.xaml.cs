using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MediaBrushes = System.Windows.Media.Brushes;
using ObsVoiceAssistant.Audio;
using ObsVoiceAssistant.Integrations.Obs;
using ObsVoiceAssistant.Integrations.Twitch;

namespace ObsVoiceAssistant.Configuration;

public partial class ObsSettingsWindow : Window
{
    private readonly SettingsService settingsService;
    private readonly TwitchService twitchService;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>>? inputNameLoader;
    private CancellationTokenSource? twitchConnectCts;
    private TwitchDeviceCode? currentDeviceCode;
    private bool twitchBusy;
    private bool capturingHotkey;

    public ObsSettingsWindow(
        SettingsService settingsService,
        TwitchService twitchService,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? inputNameLoader = null)
    {
        this.settingsService = settingsService;
        this.twitchService = twitchService;
        this.inputNameLoader = inputNameLoader;

        InitializeComponent();

        var settings = settingsService.Load();
        UrlTextBox.Text = settings.ObsUrl;
        TwitchClientIdBox.Text = settings.TwitchClientId ?? string.Empty;
        CreateMp4CopyCheckBox.IsChecked = settings.CreateMp4Copy;
        FfmpegPathBox.Text = settings.FfmpegPath ?? string.Empty;
        PushToTalkHotkeyBox.Text = string.IsNullOrWhiteSpace(settings.PushToTalkHotkey)
            ? PushToTalkHotkey.CapsLock.DisplayName
            : settings.PushToTalkHotkey;
        ViewerOverlayCheckBox.IsChecked = settings.ViewerOverlayEnabled;
        ViewerOverlayIntervalBox.Text = settings.ViewerOverlayIntervalSeconds
            .ToString(CultureInfo.InvariantCulture);

        // Каждое поле получает свой список: один и тот же источник можно указать
        // в нескольких ролях, например звук рабочего стола — и как музыку стрима,
        // и как личную. Пункт «не использовать» доступен даже без подключения к OBS.
        SetChoices(MicrophoneBox, Array.Empty<string>(), settings.AudioInputs.Microphone);
        SetChoices(BrowserBox, Array.Empty<string>(), settings.AudioInputs.Browser);
        SetChoices(StreamMusicBox, Array.Empty<string>(), settings.AudioInputs.StreamMusic);
        SetChoices(PersonalMusicBox, Array.Empty<string>(), settings.AudioInputs.PersonalMusic);

        // Без загрузчика остаётся ввод имени руками.
        LoadInputsButton.IsEnabled = inputNameLoader is not null;

        UpdateTwitchStatus();
        UpdateTwitchButtons();
    }

    private async void LoadInputs_Click(object sender, RoutedEventArgs e)
    {
        if (inputNameLoader is null)
        {
            return;
        }

        LoadInputsButton.IsEnabled = false;
        StatusText.Foreground = MediaBrushes.Black;
        StatusText.Text = "Запрашиваю список источников у OBS...";
        try
        {
            var names = await inputNameLoader(CancellationToken.None);
            FillChoices(MicrophoneBox, names);
            FillChoices(BrowserBox, names);
            FillChoices(StreamMusicBox, names);
            FillChoices(PersonalMusicBox, names);
            StatusText.Text = names.Count == 0
                ? "OBS не вернула ни одного источника."
                : $"Получено источников: {names.Count}.";
        }
        catch (Exception exception)
        {
            StatusText.Foreground = MediaBrushes.Firebrick;
            StatusText.Text = $"Не удалось получить список источников: {exception.Message}";
        }
        finally
        {
            LoadInputsButton.IsEnabled = true;
        }
    }

    private static void SetChoices(System.Windows.Controls.ComboBox comboBox, IReadOnlyList<string> names, string? selected)
    {
        comboBox.ItemsSource = ObsInputSelection.BuildChoices(names);
        comboBox.Text = selected ?? ObsInputSelection.NoneLabel;
    }

    private static void FillChoices(System.Windows.Controls.ComboBox comboBox, IReadOnlyList<string> names)
    {
        // Введённое вручную имя, которого нет в списке OBS, не теряем.
        var selected = ObsInputSelection.ToInputName(comboBox.Text);
        comboBox.ItemsSource = ObsInputSelection.BuildChoices(names);
        comboBox.Text = selected ?? ObsInputSelection.NoneLabel;
    }

    private void TwitchClientId_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!twitchBusy)
        {
            UpdateTwitchButtons();
        }
    }

    private async void ConnectTwitch_Click(object sender, RoutedEventArgs e)
    {
        if (twitchBusy)
        {
            return;
        }

        var clientId = TwitchClientIdBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            SetTwitchStatus("Укажите Client ID приложения Twitch.", isError: true);
            return;
        }

        // Client ID должен быть сохранён до получения токенов: иначе после закрытия
        // окна токены останутся привязаны к неизвестному приложению.
        var savedClientId = settingsService.Load().TwitchClientId;
        if (!string.Equals(savedClientId, clientId, StringComparison.Ordinal))
        {
            await twitchService.DisconnectAsync(revokeOnTwitch: false);
            settingsService.Save(settingsService.Load() with { TwitchClientId = clientId });
        }

        SetTwitchBusy(true);
        twitchConnectCts = new CancellationTokenSource();
        currentDeviceCode = null;
        try
        {
            SetTwitchStatus("Запрашиваю код подключения у Twitch...", isError: false);
            var deviceCode = await twitchService.BeginConnectAsync(twitchConnectCts.Token);
            currentDeviceCode = deviceCode;

            TwitchCodeText.Text = $"Код: {deviceCode.UserCode}";
            TwitchCodeText.Visibility = Visibility.Visible;
            OpenTwitchButton.Visibility = Visibility.Visible;
            SetTwitchStatus(
                "Введите код на странице Twitch и подтвердите доступ. Ожидаю подтверждения...",
                isError: false);
            TryOpenBrowser(deviceCode.VerificationUri);

            var user = await twitchService.CompleteConnectAsync(
                deviceCode,
                twitchConnectCts.Token);
            SetTwitchStatus($"Twitch подключён: {user.DisplayName}.", isError: false);
            TwitchCodeText.Visibility = Visibility.Collapsed;
            OpenTwitchButton.Visibility = Visibility.Collapsed;
            currentDeviceCode = null;
        }
        catch (OperationCanceledException)
        {
            SetTwitchStatus("Подключение Twitch отменено.", isError: false);
        }
        catch (Exception exception)
        {
            SetTwitchStatus($"Не удалось подключить Twitch: {exception.Message}", isError: true);
        }
        finally
        {
            twitchConnectCts.Dispose();
            twitchConnectCts = null;
            SetTwitchBusy(false);
        }
    }

    private async void DisconnectTwitch_Click(object sender, RoutedEventArgs e)
    {
        if (twitchBusy)
        {
            return;
        }

        SetTwitchBusy(true);
        try
        {
            await twitchService.DisconnectAsync();
            TwitchCodeText.Visibility = Visibility.Collapsed;
            OpenTwitchButton.Visibility = Visibility.Collapsed;
            currentDeviceCode = null;
            SetTwitchStatus("Twitch отключён.", isError: false);
        }
        catch (Exception exception)
        {
            SetTwitchStatus($"Не удалось отключить Twitch: {exception.Message}", isError: true);
        }
        finally
        {
            SetTwitchBusy(false);
        }
    }

    private void OpenTwitch_Click(object sender, RoutedEventArgs e)
    {
        if (currentDeviceCode is not null)
        {
            TryOpenBrowser(currentDeviceCode.VerificationUri);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (twitchBusy)
        {
            return;
        }

        var url = UrlTextBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "ws")
        {
            StatusText.Foreground = MediaBrushes.Firebrick;
            StatusText.Text = "Укажите корректный адрес ws:// OBS WebSocket.";
            return;
        }

        if (!int.TryParse(
                ViewerOverlayIntervalBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var overlayInterval) ||
            overlayInterval is < 15 or > 3600)
        {
            StatusText.Foreground = MediaBrushes.Firebrick;
            StatusText.Text = "Интервал оверлея: целое число секунд от 15 до 3600.";
            return;
        }

        var hotkeyText = PushToTalkHotkeyBox.Text.Trim();
        if (!Audio.PushToTalkHotkey.TryParse(hotkeyText, out var hotkey, out var hotkeyError))
        {
            StatusText.Foreground = MediaBrushes.Firebrick;
            StatusText.Text = hotkeyError ?? "Не удалось разобрать клавишу Push-to-talk.";
            return;
        }

        var clientId = TwitchClientIdBox.Text.Trim();
        var savedClientId = settingsService.Load().TwitchClientId;
        if (!string.Equals(savedClientId, clientId, StringComparison.Ordinal))
        {
            // Токены старого Client ID больше не подходят. Сеть здесь не нужна:
            // удаляем локальную копию, отзыв на Twitch не обязателен.
            twitchService.DisconnectAsync(revokeOnTwitch: false).GetAwaiter().GetResult();
        }

        var settings = settingsService.Load() with
        {
            ObsUrl = url,
            TwitchClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId,
            CreateMp4Copy = CreateMp4CopyCheckBox.IsChecked == true,
            FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPathBox.Text) ? null : FfmpegPathBox.Text.Trim(),
            PushToTalkHotkey = hotkey.ToSettingValue(),
            ViewerOverlayEnabled = ViewerOverlayCheckBox.IsChecked == true,
            ViewerOverlayIntervalSeconds = overlayInterval,
            AudioInputs = new ObsAudioInputs(
                Microphone: ObsInputSelection.ToInputName(MicrophoneBox.Text),
                Browser: ObsInputSelection.ToInputName(BrowserBox.Text),
                StreamMusic: ObsInputSelection.ToInputName(StreamMusicBox.Text),
                PersonalMusic: ObsInputSelection.ToInputName(PersonalMusicBox.Text))
        };
        settingsService.Save(settings);

        if (PasswordBox.Password.Length > 0)
        {
            settingsService.SaveObsPassword(PasswordBox.Password);
        }
        else
        {
            settingsService.DeleteObsPassword();
        }

        DialogResult = true;
        Close();
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (!capturingHotkey)
        {
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            capturingHotkey = false;
            CaptureHotkeyButton.Content = "Определить";
            StatusText.Foreground = MediaBrushes.Black;
            StatusText.Text = "Определение отменено.";
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var hotkey = new PushToTalkHotkey(
            virtualKey,
            modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Shift),
            modifiers.HasFlag(ModifierKeys.Alt),
            modifiers.HasFlag(ModifierKeys.Windows));

        PushToTalkHotkeyBox.Text = hotkey.DisplayName;
        capturingHotkey = false;
        CaptureHotkeyButton.Content = "Определить";
        StatusText.Foreground = MediaBrushes.Black;
        StatusText.Text = $"Клавиша записана: {hotkey.DisplayName}.";
    }

    private void CaptureHotkey_Click(object sender, RoutedEventArgs e)
    {
        capturingHotkey = !capturingHotkey;
        CaptureHotkeyButton.Content = capturingHotkey ? "Отменить" : "Определить";
        StatusText.Foreground = MediaBrushes.Black;
        StatusText.Text = capturingHotkey
            ? "Нажмите нужную клавишу или комбинацию. Esc — отменить."
            : "Определение отменено.";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите ffmpeg.exe",
            Filter = "ffmpeg (ffmpeg.exe)|ffmpeg.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            FfmpegPathBox.Text = dialog.FileName;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Отменяем ожидание Device Code, чтобы фоновый опрос не держал окно.
        twitchConnectCts?.Cancel();
        base.OnClosed(e);
    }

    private void SetTwitchBusy(bool busy)
    {
        twitchBusy = busy;
        TwitchClientIdBox.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
        UpdateTwitchButtons();
    }

    private void UpdateTwitchButtons()
    {
        var hasClientId = !string.IsNullOrWhiteSpace(TwitchClientIdBox.Text);
        ConnectTwitchButton.IsEnabled = !twitchBusy && hasClientId;
        DisconnectTwitchButton.IsEnabled = !twitchBusy && twitchService.HasSavedTokens;
    }

    private void UpdateTwitchStatus()
    {
        if (string.IsNullOrWhiteSpace(TwitchClientIdBox.Text))
        {
            SetTwitchStatus("Twitch не настроен: укажите Client ID.", isError: false);
        }
        else if (twitchService.HasSavedTokens)
        {
            SetTwitchStatus("Twitch подключён. Токен сохранён в защищённом хранилище Windows.", isError: false);
        }
        else
        {
            SetTwitchStatus("Twitch не подключён. Нажмите «Подключить Twitch».", isError: false);
        }
    }

    private void SetTwitchStatus(string message, bool isError)
    {
        TwitchStatusText.Foreground = isError ? MediaBrushes.Firebrick : MediaBrushes.Gray;
        TwitchStatusText.Text = message;
    }

    private void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            SetTwitchStatus($"Откройте страницу вручную: {url}", isError: false);
        }
    }
}
