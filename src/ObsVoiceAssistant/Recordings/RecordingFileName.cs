using System.IO;
using System.Text;

namespace ObsVoiceAssistant.Recordings;

/// <summary>
/// Имя файла после добавления пользовательского префикса. Префикс не заменяет
/// имя OBS, а ставится перед ним: «Мой клип_2026-10-20 18-16-32.mkv». Так дата
/// и время записи остаются в имени, а расширение всегда берётся из OBS.
/// </summary>
public static class RecordingFileName
{
    /// <summary>Ограничение префикса, чтобы не упереться в длину пути Windows.</summary>
    public const int MaxPrefixLength = 80;

    /// <summary>
    /// Убирает символы, недопустимые в имени файла, и обрезает префикс.
    /// Пустой результат означает, что переименовывать нечем.
    /// </summary>
    public static string SanitizePrefix(string? value)
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
        if (sanitized.Length > MaxPrefixLength)
        {
            sanitized = sanitized[..MaxPrefixLength].TrimEnd(' ', '.');
        }

        return sanitized;
    }

    /// <summary>
    /// Полный путь к переименованному файлу. Если такое имя уже занято, рядом
    /// появляется « (2)», « (3)» и так далее: существующий файл не перезаписывается.
    /// </summary>
    public static string BuildTargetPath(
        string sourcePath,
        string prefix,
        Func<string, bool>? fileExists = null)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Путь к файлу OBS не содержит каталога.", nameof(sourcePath));
        }

        var extension = Path.GetExtension(sourcePath);
        var originalName = Path.GetFileNameWithoutExtension(sourcePath);
        var baseName = $"{prefix}_{originalName}";
        var exists = fileExists ?? File.Exists;

        var candidate = Path.Combine(directory, baseName + extension);
        for (var index = 2; exists(candidate); index++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}){extension}");
        }

        return candidate;
    }
}
