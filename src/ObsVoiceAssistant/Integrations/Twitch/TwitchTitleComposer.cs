using System.Text;

namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>Итоговое название и причина отказа. Логика детерминированная, без сети.</summary>
public sealed record TwitchTitleComposition(bool IsSuccess, string Title, string? Error)
{
    public static TwitchTitleComposition Success(string title) => new(true, title, null);

    public static TwitchTitleComposition Failure(string error) => new(false, string.Empty, error);
}

/// <summary>
/// Собирает новое название с тегами текущего. Тег — слово, начинающееся с «!».
/// Теги добавляются в конец в порядке старого названия и без дублей; регистр
/// при сравнении не учитывается. Текст пользователя не обрезается.
/// </summary>
public static class TwitchTitleComposer
{
    public const int MaxTitleLength = 140;

    /// <summary>Пунктуация, которая не может быть частью тега в конце слова.</summary>
    private static readonly char[] TagTrimChars =
    {
        '.', ',', ';', ':', '!', '?', '…', ')', ']', '}', '»', '"', '\''
    };

    /// <summary>Пунктуация в конце названия: для Twitch она не несёт смысла.</summary>
    private static readonly char[] TitleTrimChars = { '.', ',', ';', ':', '!', '?', '…' };

    public static TwitchTitleComposition Compose(string? newTitle, string? currentTitle)
    {
        var title = NormalizeTitle(newTitle);
        if (title.Length == 0)
        {
            return TwitchTitleComposition.Failure("Не указано новое название трансляции.");
        }

        var seenTags = new HashSet<string>(ExtractTags(title), StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var tag in ExtractTags(currentTitle))
        {
            if (seenTags.Add(tag))
            {
                missing.Add(tag);
            }
        }

        var composed = missing.Count == 0
            ? title
            : $"{title} {string.Join(' ', missing)}";

        return composed.Length > MaxTitleLength
            ? TwitchTitleComposition.Failure(
                $"Название трансляции вместе с тегами длиннее {MaxTitleLength} символов.")
            : TwitchTitleComposition.Success(composed);
    }

    /// <summary>
    /// Выделяет теги в порядке появления. Хвостовая пунктуация отбрасывается,
    /// чтобы «!tg,» и «!tg» считались одним тегом.
    /// </summary>
    public static IReadOnlyList<string> ExtractTags(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Array.Empty<string>();
        }

        var tags = new List<string>();
        foreach (var token in title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = token.TrimEnd(TagTrimChars);
            if (candidate.Length < 2 || candidate[0] != '!')
            {
                continue;
            }

            if (candidate.Skip(1).Any(char.IsLetterOrDigit))
            {
                tags.Add(candidate);
            }
        }

        return tags;
    }

    /// <summary>Схлопывает пробелы и убирает пунктуацию в конце названия.</summary>
    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(title.Length);
        var previousWasSpace = false;

        foreach (var symbol in title.Trim())
        {
            if (char.IsWhiteSpace(symbol))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(symbol);
            previousWasSpace = false;
        }

        return builder.ToString().TrimEnd(TitleTrimChars).TrimEnd();
    }
}
