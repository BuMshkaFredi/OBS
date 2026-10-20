using System.Diagnostics;
using System.IO;
using ObsVoiceAssistant.Audio;
using Vosk;

namespace ObsVoiceAssistant.Speech;

/// <summary>
/// Локальное распознавание через Vosk. Движок рассчитан на CPU: компактная русская
/// модель занимает десятки мегабайт и обрабатывает короткую команду за доли секунды.
/// GPU не требуется, аудио не покидает компьютер.
/// </summary>
public sealed class VoskSpeechRecognizer : ISpeechRecognizer, IDisposable
{
    /// <summary>
    /// Каталоги русских моделей в порядке предпочтения. Если установлена более крупная модель,
    /// используется она: точность выше, но и распознавание идёт медленнее.
    /// </summary>
    public static readonly IReadOnlyList<string> ModelDirectoryNames = new[]
    {
        "vosk-model-ru-0.42",
        "vosk-model-ru-0.22",
        "vosk-model-small-ru-0.22"
    };

    /// <summary>Имя каталога модели по умолчанию, если ничего не найдено.</summary>
    public static string DefaultModelDirectoryName => ModelDirectoryNames[^1];

    private readonly string modelDirectory;
    private readonly Action<string>? recognizedText;
    private readonly Action<string>? diagnostic;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private Model? model;
    private bool disposed;

    public VoskSpeechRecognizer(
        string? modelDirectory = null,
        Action<string>? recognizedText = null,
        Action<string>? diagnostic = null)
    {
        this.modelDirectory = modelDirectory ?? ResolveDefaultModelDirectory();
        this.recognizedText = recognizedText;
        this.diagnostic = diagnostic;
    }

    /// <summary>
    /// Каталог модели: приоритет задаётся списком имён, а поиск идёт рядом с приложением,
    /// вверх по дереву каталогов и в текущем каталоге, чтобы находить модель и в bin,
    /// и в корне репозитория при dotnet run.
    /// </summary>
    public static string ResolveDefaultModelDirectory() =>
        FindModelDirectory(EnumerateSearchRoots(AppContext.BaseDirectory), ModelDirectoryNames)
        ?? Path.Combine(AppContext.BaseDirectory, "models", DefaultModelDirectoryName);

    /// <summary>
    /// Ищет первый существующий каталог модели. Сначала перебираются имена моделей по приоритету,
    /// затем корневые каталоги, поэтому более крупная установленная модель побеждает копию в bin.
    /// </summary>
    public static string? FindModelDirectory(
        IEnumerable<string> rootDirectories,
        IReadOnlyList<string> modelDirectoryNames)
    {
        var roots = rootDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var name in modelDirectoryNames)
        {
            foreach (var root in roots)
            {
                var candidate = Path.Combine(root, "models", name);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public Task<string?> RecognizeAsync(AudioRecording recording, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (recording.Data.IsEmpty)
        {
            return Task.FromResult<string?>(null);
        }

        EnsureModel();

        // Vosk принимает сырой 16-битный моно PCM, поэтому заголовок WAV нужно срезать.
        var pcm = PcmAudio.SlicePcm16(recording.Data.Span).ToArray();

        var stopwatch = Stopwatch.StartNew();
        string? text;
        using (var recognizer = new VoskRecognizer(model!, recording.SampleRate))
        {
            recognizer.AcceptWaveform(pcm, pcm.Length);
            text = VoskResultParser.ParseText(recognizer.FinalResult());
        }

        stopwatch.Stop();
        diagnostic?.Invoke($"Распознавание заняло {stopwatch.ElapsedMilliseconds} мс");

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<string?>(null);
        }

        recognizedText?.Invoke(text);
        return Task.FromResult<string?>(text);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        model?.Dispose();
        initializationLock.Dispose();
    }

    private void EnsureModel()
    {
        if (model is not null)
        {
            return;
        }

        initializationLock.Wait();
        try
        {
            if (model is not null)
            {
                return;
            }

            if (!Directory.Exists(modelDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"\u041d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d\u0430 \u043b\u043e\u043a\u0430\u043b\u044c\u043d\u0430\u044f \u043c\u043e\u0434\u0435\u043b\u044c \u0440\u0443\u0441\u0441\u043a\u043e\u0433\u043e \u0440\u0430\u0441\u043f\u043e\u0437\u043d\u0430\u0432\u0430\u043d\u0438\u044f Vosk: {modelDirectory}");
            }

            // Vosk пишет собственные native-логи в stderr; приложению они не нужны.
            Vosk.Vosk.SetLogLevel(-1);
            var loadWatch = Stopwatch.StartNew();
            model = new Model(modelDirectory);
            loadWatch.Stop();
            diagnostic?.Invoke(
                $"Модель распознавания: {modelDirectory} (Vosk), загрузка заняла {loadWatch.ElapsedMilliseconds} мс");
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots(string baseDirectory)
    {
        yield return baseDirectory;

        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }

        yield return Environment.CurrentDirectory;
    }
}
