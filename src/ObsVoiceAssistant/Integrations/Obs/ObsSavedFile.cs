namespace ObsVoiceAssistant.Integrations.Obs;

/// <summary>Тип файла, который OBS только что записал на диск.</summary>
public enum ObsSavedFileKind
{
    Recording,
    Replay
}

/// <summary>Готовый файл записи или повтора вместе с путём, который дал OBS.</summary>
public sealed record ObsSavedFile(string FilePath, ObsSavedFileKind Kind);

/// <summary>
/// События OBS о готовых файлах. Отдельный интерфейс нужен, чтобы тестовые
/// заглушки <see cref="IObsClient"/> не обязаны были реализовывать события.
/// </summary>
public interface IObsSavedFileSource
{
    event Action<ObsSavedFile>? SavedFile;
}
