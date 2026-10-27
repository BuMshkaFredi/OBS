using System.IO;
using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.Recordings;

/// <summary>
/// Предлагает имя для готового файла записи или повтора и переименовывает его
/// рядом с файлом OBS. Окно открывается на каждое событие OBS, включая ручную
/// остановку записи и сохранение повтора в самом OBS.
/// </summary>
public sealed class RecordingNamingService : IDisposable
{
    private const int MoveAttempts = 3;
    private static readonly TimeSpan MoveRetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly IRecordingNamePrompt prompt;
    private readonly Action<string> notify;
    private readonly Action<string>? log;
    private readonly IMp4CopyService? mp4Copy;
    private readonly SemaphoreSlim promptLock = new(1, 1);
    private IObsSavedFileSource? source;
    private bool disposed;

    public RecordingNamingService(
        IRecordingNamePrompt prompt,
        Action<string> notify,
        Action<string>? log = null,
        IMp4CopyService? mp4Copy = null)
    {
        this.prompt = prompt;
        this.notify = notify;
        this.log = log;
        this.mp4Copy = mp4Copy;
    }

    public void Attach(IObsSavedFileSource savedFiles)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        source = savedFiles;
        savedFiles.SavedFile += OnSavedFile;
    }

    /// <summary>
    /// Обрабатывает один готовый файл. Публичный метод нужен тестам: в приложении
    /// его вызывает подписка на события OBS.
    /// </summary>
    public async Task HandleAsync(ObsSavedFile file)
    {
        // Два окна одновременно перекрыли бы друг друга, поэтому запросы идут по очереди.
        await promptLock.WaitAsync().ConfigureAwait(false);
        string? finalPath;
        try
        {
            finalPath = await HandleCoreAsync(file).ConfigureAwait(false);
        }
        finally
        {
            promptLock.Release();
        }

        // Конвертация идёт вне блокировки: она может занять минуты на длинной
        // записи, и следующий файл не должен ждать её из-за окна переименования.
        if (finalPath is not null)
        {
            await CreateMp4CopyAsync(finalPath, file.Kind).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Переименовывает файл и возвращает путь к готовому файлу, для которого
    /// нужна MP4-копия. null — копировать нечего.
    /// </summary>
    private async Task<string?> HandleCoreAsync(ObsSavedFile file)
    {
        var fileName = Path.GetFileName(file.FilePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            log?.Invoke($"OBS не дал имя файла {KindName(file.Kind)}: «{file.FilePath}»");
            return null;
        }

        if (!File.Exists(file.FilePath))
        {
            // Файл могли удалить или OBS ещё не дописал его на медленный диск.
            log?.Invoke($"Файл {KindName(file.Kind)} не найден: {fileName}");
            notify($"{NotRenamedMessage(file.Kind)}: файл не найден");
            return null;
        }

        var enteredName = await prompt.AskNameAsync(file.Kind, fileName).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(enteredName))
        {
            log?.Invoke($"Имя {KindName(file.Kind)} не изменено: {fileName}");
            return file.FilePath;
        }

        var sanitized = RecordingFileName.SanitizeName(enteredName);
        if (sanitized.Length == 0)
        {
            log?.Invoke("Имя не изменено: после очистки не осталось допустимых символов");
            notify("Название не изменено: в нём нет допустимых символов");
            return file.FilePath;
        }

        var targetPath = RecordingFileName.BuildTargetPath(file.FilePath, sanitized);
        try
        {
            await MoveWithRetryAsync(file.FilePath, targetPath).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log?.Invoke(
                $"Не удалось переименовать файл {KindName(file.Kind)} {fileName}: " +
                $"{exception.GetType().Name}: {exception.Message}");
            notify($"{NotRenamedMessage(file.Kind)}: {exception.Message}");
            // Файл записи всё равно готов: копию MP4 делаем из оригинала.
            return file.FilePath;
        }

        var targetName = Path.GetFileName(targetPath);
        log?.Invoke($"{RenamedMessage(file.Kind)}: {fileName} -> {targetName}");
        notify($"{RenamedMessage(file.Kind)}: {targetName}");

        return targetPath;
    }

    /// <summary>
    /// Создаёт MP4-копию готового файла. Ошибка копирования не отменяет
    /// переименование: пользователь узнает о проблеме из уведомления.
    /// </summary>
    private async Task CreateMp4CopyAsync(string filePath, ObsSavedFileKind kind)
    {
        if (mp4Copy is null)
        {
            return;
        }

        Mp4CopyResult result;
        try
        {
            result = await mp4Copy.CreateCopyAsync(filePath).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log?.Invoke(
                $"Ошибка создания MP4-копии {KindName(kind)}: " +
                $"{exception.GetType().Name}: {exception.Message}");
            notify($"MP4-копия не создана: {exception.Message}");
            return;
        }

        switch (result.Status)
        {
            case Mp4CopyStatus.Created:
                var name = Path.GetFileName(result.TargetPath);
                log?.Invoke($"MP4-копия создана: {name}");
                notify($"MP4-копия создана: {name}");
                break;

            case Mp4CopyStatus.AlreadyMp4:
                log?.Invoke($"Файл уже в MP4, копия не нужна: {Path.GetFileName(filePath)}");
                break;

            case Mp4CopyStatus.Disabled:
                break;

            default:
                log?.Invoke($"MP4-копия не создана: {result.Error}");
                notify($"MP4-копия не создана: {result.Error}");
                break;
        }
    }

    private static async Task MoveWithRetryAsync(string sourcePath, string targetPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(sourcePath, targetPath);
                return;
            }
            catch (IOException) when (attempt < MoveAttempts)
            {
                // OBS иногда отпускает файл чуть позже события; короткая пауза это решает.
                await Task.Delay(MoveRetryDelay).ConfigureAwait(false);
            }
        }
    }

    private void OnSavedFile(ObsSavedFile file)
    {
        // Событие OBS приходит из фонового потока: обработчик не должен его тормозить.
        _ = HandleSafelyAsync(file);
    }

    private async Task HandleSafelyAsync(ObsSavedFile file)
    {
        try
        {
            await HandleAsync(file).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log?.Invoke(
                $"Ошибка обработки файла {KindName(file.Kind)}: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string KindName(ObsSavedFileKind kind) =>
        kind == ObsSavedFileKind.Replay ? "повтора" : "записи";

    private static string RenamedMessage(ObsSavedFileKind kind) =>
        kind == ObsSavedFileKind.Replay ? "Повтор переименован" : "Запись переименована";

    private static string NotRenamedMessage(ObsSavedFileKind kind) =>
        kind == ObsSavedFileKind.Replay ? "Повтор не переименован" : "Запись не переименована";

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (source is not null)
        {
            source.SavedFile -= OnSavedFile;
            source = null;
        }
    }
}
