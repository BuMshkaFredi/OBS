using System.Windows;
using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.Recordings;

public partial class RecordingNameWindow : Window
{
    private readonly TaskCompletionSource<string?> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RecordingNameWindow(ObsSavedFileKind kind, string fileName)
    {
        InitializeComponent();
        HeaderText.Text = kind == ObsSavedFileKind.Replay ? "Название повтора" : "Название записи";
        FileText.Text = $"Файл OBS: {fileName}";

        Loaded += OnLoaded;
        Closed += (_, _) => completion.TrySetResult(null);
    }

    /// <summary>Введённый префикс или <c>null</c>, если окно закрыли без ответа.</summary>
    public Task<string?> Completion => completion.Task;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // Сначала результат, потом закрытие: обработчик Closed не должен его перетереть.
        completion.TrySetResult(NameBox.Text);
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Close();
}
