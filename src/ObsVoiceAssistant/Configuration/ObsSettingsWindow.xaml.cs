using System.Windows;
using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.Configuration;

public partial class ObsSettingsWindow : Window
{
    private readonly SettingsService settingsService;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>>? inputNameLoader;

    public ObsSettingsWindow(
        SettingsService settingsService,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? inputNameLoader = null)
    {
        InitializeComponent();
        this.settingsService = settingsService;
        this.inputNameLoader = inputNameLoader;

        var settings = settingsService.Load();
        UrlTextBox.Text = settings.ObsUrl;

        // Каждое поле получает свой список: один и тот же источник можно указать
        // в нескольких ролях, например звук рабочего стола — и как музыку стрима,
        // и как личную. Пункт «не использовать» доступен даже без подключения к OBS.
        SetChoices(MicrophoneBox, Array.Empty<string>(), settings.AudioInputs.Microphone);
        SetChoices(BrowserBox, Array.Empty<string>(), settings.AudioInputs.Browser);
        SetChoices(StreamMusicBox, Array.Empty<string>(), settings.AudioInputs.StreamMusic);
        SetChoices(PersonalMusicBox, Array.Empty<string>(), settings.AudioInputs.PersonalMusic);

        // Без загрузчика остаётся ввод имени руками.
        LoadInputsButton.IsEnabled = inputNameLoader is not null;
    }

    private async void LoadInputs_Click(object sender, RoutedEventArgs e)
    {
        if (inputNameLoader is null)
        {
            return;
        }

        LoadInputsButton.IsEnabled = false;
        StatusText.Foreground = System.Windows.Media.Brushes.Black;
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
            StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "ws")
        {
            StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            StatusText.Text = "Укажите корректный адрес ws:// OBS WebSocket.";
            return;
        }

        var settings = settingsService.Load() with
        {
            ObsUrl = url,
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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
