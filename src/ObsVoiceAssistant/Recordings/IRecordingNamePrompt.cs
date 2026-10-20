using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.Recordings;

/// <summary>
/// Запрашивает у пользователя префикс имени файла. Пустая строка или <c>null</c>
/// означают «оставить имя OBS»: без ответа файл не переименовывается.
/// </summary>
public interface IRecordingNamePrompt
{
    Task<string?> AskNameAsync(ObsSavedFileKind kind, string fileName);
}
