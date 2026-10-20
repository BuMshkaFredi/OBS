using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.Recordings;

/// <summary>
/// Показывает WPF-окно ввода на UI-потоке: событие OBS приходит из фонового
/// потока, а окно можно создавать только в диспетчере приложения.
/// </summary>
public sealed class WpfRecordingNamePrompt : IRecordingNamePrompt
{
    public Task<string?> AskNameAsync(ObsSavedFileKind kind, string fileName)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return Task.FromResult<string?>(null);
        }

        return dispatcher
            .InvokeAsync(() => ShowAsync(kind, fileName))
            .Task
            .Unwrap();
    }

    private static Task<string?> ShowAsync(ObsSavedFileKind kind, string fileName)
    {
        var window = new RecordingNameWindow(kind, fileName);
        window.Show();
        return window.Completion;
    }
}
