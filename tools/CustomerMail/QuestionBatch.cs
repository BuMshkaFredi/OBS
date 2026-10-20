using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomerMail;

internal sealed class QuestionBatch
{
    public string Id { get; init; } = string.Empty;

    public string Topic { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string? CreatedAt { get; init; }

    public string? ApprovedAt { get; init; }

    public List<Question> Questions { get; init; } = [];

    public static QuestionBatch? TryLoad(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<QuestionBatch>(File.ReadAllText(path), JsonFiles.Options);
        }
        catch (JsonException ex)
        {
            error = $"Файл {Path.GetFileName(path)} не соответствует схеме: {ex.Message}";
            return null;
        }
    }
}

internal sealed class Question
{
    public string Id { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;
}

internal static class BatchTopics
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["obs-behavior"] = "поведение OBS",
        ["audio"] = "аудио и источники",
        ["notifications"] = "уведомления и подтверждения",
        ["twitch"] = "Twitch",
        ["ux"] = "удобство использования",
        ["performance"] = "производительность и задержка",
    };

    public static bool TryGetLabel(string? topic, out string label)
    {
        label = string.Empty;
        return topic is not null && Labels.TryGetValue(topic, out label!);
    }
}
