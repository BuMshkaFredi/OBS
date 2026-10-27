using System.Text.Json;

namespace CustomerMail;

/// <summary>
/// Письмо о готовой сборке. В отличие от партии вопросов, здесь есть инструкции
/// и ссылки на публичный релиз, поэтому схема отдельная и проверяется отдельно.
/// </summary>
internal sealed class ReleaseMail
{
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? CreatedAt { get; init; }

    public string? ApprovedAt { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string ReleaseUrl { get; init; } = string.Empty;

    public string DownloadUrl { get; init; } = string.Empty;

    /// <summary>
    /// SHA-256 архива. Держится отдельным полем, потому что длинная hex-строка
    /// в свободном тексте запрещена защитой от утечек.
    /// </summary>
    public string ArchiveSha256 { get; init; } = string.Empty;

    public List<string> Intro { get; init; } = [];

    public List<ReleaseSection> Sections { get; init; } = [];

    public List<string> Closing { get; init; } = [];

    public static ReleaseMail? TryLoad(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReleaseMail>(File.ReadAllText(path), JsonFiles.Options);
        }
        catch (JsonException ex)
        {
            error = $"Файл {Path.GetFileName(path)} не соответствует схеме: {ex.Message}";
            return null;
        }
    }
}

internal sealed class ReleaseSection
{
    public string Title { get; init; } = string.Empty;

    public List<string> Paragraphs { get; init; } = [];

    public List<string> Bullets { get; init; } = [];

    public List<string> Steps { get; init; } = [];
}
