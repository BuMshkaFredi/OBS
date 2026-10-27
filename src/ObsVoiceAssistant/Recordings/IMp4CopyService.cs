namespace ObsVoiceAssistant.Recordings;

/// <summary>
/// Настройки создания MP4-копии. Читаются при каждом сохранённом файле,
/// поэтому новая настройка применяется без перезапуска приложения.
/// </summary>
/// <param name="Enabled">Создавать ли копию вообще.</param>
/// <param name="FfmpegPath">Путь к ffmpeg.exe. Если пусто, программа ищется рядом с приложением и в PATH.</param>
public sealed record Mp4CopyOptions(bool Enabled = true, string? FfmpegPath = null);

/// <summary>Итог создания MP4-копии.</summary>
public enum Mp4CopyStatus
{
    /// <summary>Копия создана.</summary>
    Created,

    /// <summary>Исходный файл уже в MP4, копия не нужна.</summary>
    AlreadyMp4,

    /// <summary>Создание копии выключено в настройках.</summary>
    Disabled,

    /// <summary>Копию создать не удалось, в Error — причина.</summary>
    Failed
}

/// <summary>Результат создания MP4-копии.</summary>
public sealed record Mp4CopyResult(Mp4CopyStatus Status, string? TargetPath = null, string? Error = null)
{
    public bool Created => Status == Mp4CopyStatus.Created;
}

/// <summary>
/// Создаёт MP4-копию готового файла записи или повтора. Отдельный интерфейс
/// нужен, чтобы тесты не зависели от установленного ffmpeg.
/// </summary>
public interface IMp4CopyService
{
    Task<Mp4CopyResult> CreateCopyAsync(string sourcePath, CancellationToken cancellationToken = default);
}
