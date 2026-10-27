using System.IO;
using System.Text;

namespace ObsVoiceAssistant.Recordings;

/// <summary>
/// Имя файла после переименования по названию пользователя. Название заменяет
/// автоматическое имя OBS целиком: «Мой клип.mkv» вместо
/// «Мой клип_2026-10-20 18-16-32.mkv». Расширение и папка остаются от OBS:
/// формат записи меняется только перекодированием, а его не делает.
/// </summary>
public static class RecordingFileName
{
    /// <summary>Ограничение названия, чтобы не упереться в длину пути Windows.</summary>
    public const int MaxNameLength = 80;

    /// <summary>
    /// Расширения видео, которые пользователь мог ввести вместе с названием.
    /// Они отбрасываются: формат файла задаёт OBS.
    /// </summary>
    private static readonly string[] VideoExtensions =
    {
        ".mkv",
        ".mp4",
        ".mov",
        ".avi",
        ".webm",
        ".flv",
        ".m4v"
    };

    /// <summary>
    /// Убирает символы, недопустимые в имени файла, отбрасывает лишнее
    /// расширение и обрезает название. Пустой результат означает,
    /// что переименовывать нечем.
    /// </summary>
    public static string SanitizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var symbol in value.Trim())
        {
            builder.Append(Array.IndexOf(invalid, symbol) >= 0 ? '_' : symbol);
        }

        // Точка и пробел в конце имени Windows не допускает.
        var sanitized = builder.ToString().Trim(' ', '.');
        sanitized = RemoveVideoExtension(sanitized);
        if (sanitized.Length > MaxNameLength)
        {
            sanitized = sanitized[..MaxNameLength].TrimEnd(' ', '.');
        }

        return sanitized;
    }

    /// <summary>
    /// Убирает расширение видео, если пользователь ввёл его вместе с названием:
    /// «клип.mp4» → «клип». Формат файла задаёт OBS, поэтому лишнее расширение
    /// только путает.
    /// </summary>
    private static string RemoveVideoExtension(string name)
    {
        var extension = Path.GetExtension(name);
        if (extension.Length == 0)
        {
            return name;
        }

        return VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? name[..^extension.Length].TrimEnd(' ', '.')
            : name;
    }

    /// <summary>
    /// Полный путь к переименованному файлу. Название пользователя заменяет имя
    /// OBS целиком, расширение берётся из исходного файла. Если такое имя уже
    /// занято, рядом появляется « (2)», « (3)» и так далее: существующий файл
    /// не перезаписывается.
    /// </summary>
    public static string BuildTargetPath(
        string sourcePath,
        string name,
        Func<string, bool>? fileExists = null)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Путь к файлу OBS не содержит каталога.", nameof(sourcePath));
        }

        var extension = Path.GetExtension(sourcePath);
        var baseName = name;
        var exists = fileExists ?? File.Exists;

        var candidate = Path.Combine(directory, baseName + extension);
        for (var index = 2; exists(candidate); index++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
        }

        return candidate;
    }

    /// <summary>
    /// Путь для копии файла с другим расширением рядом с оригиналом:
    /// «2026-10-20 18-16-32.mkv» → «2026-10-20 18-16-32.mp4».
    /// Если такой файл уже есть, добавляется « (2)», « (3)» и так далее.
    /// </summary>
    public static string BuildSiblingPath(
        string sourcePath,
        string extension,
        Func<string, bool>? fileExists = null)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Путь к файлу OBS не содержит каталога.", nameof(sourcePath));
        }

        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var exists = fileExists ?? File.Exists;

        var candidate = Path.Combine(directory, baseName + extension);
        for (var index = 2; exists(candidate); index++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
        }

        return candidate;
    }
}
