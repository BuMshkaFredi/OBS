using System.Text.Json;

namespace CustomerMail;

internal sealed class AuditEntry
{
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>send или receive.</summary>
    public string Action { get; init; } = string.Empty;

    public string BatchId { get; init; } = string.Empty;

    /// <summary>questions для партии вопросов, release для письма о сборке.</summary>
    public string Kind { get; init; } = string.Empty;

    public string Recipient { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string BodySha256 { get; init; } = string.Empty;

    public int QuestionCount { get; init; }

    /// <summary>sent, failed или saved.</summary>
    public string Result { get; init; } = string.Empty;

    public string? MessageId { get; init; }
}

internal static class AuditLog
{
    public static string PathFor(string root) => Path.Combine(RepoLayout.AuditDirectory(root), "mail-log.jsonl");

    public static void Append(string root, AuditEntry entry)
    {
        var path = PathFor(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(entry, JsonFiles.Options);
        File.AppendAllText(path, line + Environment.NewLine);
    }

    public static IReadOnlyList<AuditEntry> ReadAll(string root)
    {
        var path = PathFor(root);
        if (!File.Exists(path))
        {
            return [];
        }

        var entries = new List<AuditEntry>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonSerializer.Deserialize<AuditEntry>(line, JsonFiles.Options)
                ?? throw new InvalidOperationException($"Пустая запись в журнале, строка {lineNumber}.");
            entries.Add(entry);
        }

        return entries;
    }

    public static bool WasSent(string root, string batchId) =>
        ReadAll(root).Any(e =>
            string.Equals(e.Action, "send", StringComparison.Ordinal)
            && string.Equals(e.Result, "sent", StringComparison.Ordinal)
            && string.Equals(e.BatchId, batchId, StringComparison.Ordinal));

    public static int CountSentOnUtcDate(string root, DateOnly date) =>
        ReadAll(root).Count(e =>
            string.Equals(e.Action, "send", StringComparison.Ordinal)
            && string.Equals(e.Result, "sent", StringComparison.Ordinal)
            && DateOnly.FromDateTime(e.TimestampUtc.UtcDateTime) == date);

    public static IReadOnlySet<string> ReceivedMessageIds(string root) =>
        ReadAll(root)
            .Where(e => string.Equals(e.Action, "receive", StringComparison.Ordinal) && !string.IsNullOrEmpty(e.MessageId))
            .Select(e => e.MessageId!)
            .ToHashSet(StringComparer.Ordinal);
}
