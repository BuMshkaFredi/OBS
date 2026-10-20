using System.Text.Json;

namespace ObsVoiceAssistant.Speech;

/// <summary>
/// Разбор JSON-ответа Vosk вида <c>{"text": "начни запись"}</c>.
/// Вынесен отдельно, чтобы проверяться тестами без загрузки модели.
/// </summary>
public static class VoskResultParser
{
    public static string? ParseText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = textElement.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
