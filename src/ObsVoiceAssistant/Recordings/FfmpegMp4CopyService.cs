using System.Diagnostics;
using System.IO;

namespace ObsVoiceAssistant.Recordings;

/// <summary>Результат запуска внешней программы.</summary>
public sealed record ProcessResult(int ExitCode, string ErrorOutput);

/// <summary>
/// Запуск внешней программы. Отдельный интерфейс, чтобы тесты проверяли
/// создание копии без установленного ffmpeg.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Запуск программы средствами операционной системы.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Не удалось запустить {Path.GetFileName(fileName)}.");

        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        await outputTask.ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, error);
    }
}

/// <summary>Поиск ffmpeg.exe: явный путь, папка рядом с программой, затем PATH.</summary>
public static class FfmpegLocator
{
    public const string ExecutableName = "ffmpeg.exe";

    /// <summary>
    /// Возвращает полный путь к ffmpeg.exe или null, если программа не найдена.
    /// </summary>
    public static string? Find(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                var full = Path.GetFullPath(configuredPath.Trim());
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Некорректный путь в настройках не должен ломать сохранение записи.
            }
        }

        foreach (var candidate in DefaultCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath();
    }

    private static IEnumerable<string> DefaultCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "ffmpeg", ExecutableName);
        yield return Path.Combine(baseDirectory, ExecutableName);
    }

    private static string? FindOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim(), ExecutableName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// Создаёт MP4-копию файла записи или повтора через ffmpeg. Файл не перекодируется:
/// потоки копируются как есть, поэтому качество не меняется и операция быстрая.
/// </summary>
public sealed class FfmpegMp4CopyService : IMp4CopyService
{
    private readonly Func<Mp4CopyOptions> optionsProvider;
    private readonly IProcessRunner processRunner;
    private readonly Func<string?, string?> locateFfmpeg;
    private readonly Action<string>? log;

    public FfmpegMp4CopyService(
        Func<Mp4CopyOptions> optionsProvider,
        IProcessRunner? processRunner = null,
        Action<string>? log = null,
        Func<string?, string?>? locateFfmpeg = null)
    {
        this.optionsProvider = optionsProvider;
        this.processRunner = processRunner ?? new SystemProcessRunner();
        this.log = log;
        this.locateFfmpeg = locateFfmpeg ?? FfmpegLocator.Find;
    }

    public async Task<Mp4CopyResult> CreateCopyAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var options = optionsProvider();
        if (!options.Enabled)
        {
            return new Mp4CopyResult(Mp4CopyStatus.Disabled);
        }

        if (string.Equals(Path.GetExtension(sourcePath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            // OBS сохраняет повтор сразу в MP4: копировать файл в себя незачем.
            log?.Invoke($"Файл уже в MP4, копия не нужна: {Path.GetFileName(sourcePath)}");
            return new Mp4CopyResult(Mp4CopyStatus.AlreadyMp4);
        }

        var ffmpeg = locateFfmpeg(options.FfmpegPath);
        if (string.IsNullOrEmpty(ffmpeg))
        {
            const string error =
                "Не найден ffmpeg. Укажите путь к ffmpeg.exe в настройках " +
                "или положите ffmpeg.exe в папку ffmpeg рядом с программой.";
            log?.Invoke(error);
            return new Mp4CopyResult(Mp4CopyStatus.Failed, null, error);
        }

        var targetPath = RecordingFileName.BuildSiblingPath(sourcePath, ".mp4");
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-nostdin",
            "-y",
            "-i", sourcePath,
            "-c", "copy",
            targetPath
        };

        log?.Invoke($"Создаю MP4-копию: {Path.GetFileName(sourcePath)} -> {Path.GetFileName(targetPath)}");

        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(ffmpeg, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            log?.Invoke($"Ошибка запуска ffmpeg: {exception.GetType().Name}: {exception.Message}");
            return new Mp4CopyResult(
                Mp4CopyStatus.Failed,
                null,
                $"{exception.GetType().Name}: {exception.Message}");
        }

        if (result.ExitCode != 0)
        {
            var reason = Summarize(result.ErrorOutput);
            log?.Invoke($"ffmpeg завершился с кодом {result.ExitCode}: {reason}");
            return new Mp4CopyResult(Mp4CopyStatus.Failed, null, reason);
        }

        if (!File.Exists(targetPath))
        {
            const string error = "ffmpeg отработал без ошибки, но файл не появился.";
            log?.Invoke(error);
            return new Mp4CopyResult(Mp4CopyStatus.Failed, null, error);
        }

        return new Mp4CopyResult(Mp4CopyStatus.Created, targetPath);
    }

    /// <summary>Берёт последнюю осмысленную строку вывода ffmpeg: она понятнее полного лога.</summary>
    private static string Summarize(string errorOutput)
    {
        if (string.IsNullOrWhiteSpace(errorOutput))
        {
            return "ffmpeg не сообщил причину ошибки.";
        }

        var lines = errorOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        return lines.Count == 0
            ? "ffmpeg не сообщил причину ошибки."
            : lines[^1];
    }
}
