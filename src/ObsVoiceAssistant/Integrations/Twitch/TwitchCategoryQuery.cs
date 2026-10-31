namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>
/// Преобразует произнесённое название категории в варианты для поиска Twitch.
/// Распознавание русское, а названия категорий на Twitch почти всегда латиницей
/// («дота два» вместо «Dota 2»), поэтому поиск идёт по нескольким вариантам.
/// Логика детерминированная, без сети и без словарей конкретных игр.
/// </summary>
public static class TwitchCategoryQuery
{
    /// <summary>
    /// Варианты запроса по порядку: как сказали, транслитерация и та же
    /// транслитерация с цифрами вместо числительных. Дубликаты убраны.
    /// </summary>
    public static IReadOnlyList<string> BuildVariants(string? query)
    {
        var trimmed = query?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Array.Empty<string>();
        }

        var variants = new List<string> { trimmed };
        if (ContainsCyrillic(trimmed))
        {
            AddIfNew(variants, Transliterate(trimmed));
            // Числительные сначала записываются цифрами, потом транслитерируются:
            // иначе «дота два» даёт «dota dva» и никогда не находит «Dota 2».
            AddIfNew(variants, Transliterate(ReplaceNumberWords(trimmed)));
        }

        AddIfNew(variants, ReplaceNumberWords(trimmed));
        return variants;
    }

    private static void AddIfNew(List<string> variants, string candidate)
    {
        var normalized = candidate.Trim();
        if (normalized.Length == 0)
        {
            return;
        }

        if (variants.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        variants.Add(normalized);
    }

    private static bool ContainsCyrillic(string value) =>
        value.Any(symbol => symbol is >= 'А' and <= 'я' or 'Ё' or 'ё');

    /// <summary>
    /// Числительные записываются цифрами: Twitch ищет «Dota 2», а не «dota dva».
    /// Порядок важен: «одиннадцать» должно замениться раньше «один».
    /// </summary>
    private static string ReplaceNumberWords(string value)
    {
        var result = value;
        foreach (var (word, digit) in NumberWords)
        {
            result = ReplaceWord(result, word, digit);
        }

        return result;
    }

    private static string ReplaceWord(string value, string word, string replacement)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < words.Length; index++)
        {
            if (string.Equals(words[index], word, StringComparison.OrdinalIgnoreCase))
            {
                words[index] = replacement;
            }
        }

        return string.Join(' ', words);
    }

    private static readonly (string Word, string Digit)[] NumberWords =
    {
        ("одиннадцать", "11"), ("двенадцать", "12"), ("тринадцать", "13"),
        ("четырнадцать", "14"), ("пятнадцать", "15"), ("шестнадцать", "16"),
        ("семнадцать", "17"), ("восемнадцать", "18"), ("девятнадцать", "19"),
        ("двадцать", "20"), ("тридцать", "30"), ("сорок", "40"), ("пятьдесят", "50"),
        ("шестьдесят", "60"), ("семьдесят", "70"), ("восемьдесят", "80"),
        ("девяносто", "90"), ("сто", "100"),
        ("ноль", "0"), ("один", "1"), ("одна", "1"), ("одну", "1"), ("два", "2"),
        ("две", "2"), ("три", "3"), ("четыре", "4"), ("пять", "5"), ("шесть", "6"),
        ("семь", "7"), ("восемь", "8"), ("девять", "9"), ("десять", "10")
    };

    /// <summary>
    /// Простая транслитерация ГОСТ-подобным набором. Достаточна, чтобы найти
    /// латинское название категории, и не претендует на обратимость.
    /// </summary>
    private static string Transliterate(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var symbol = value[index];
            var lower = char.ToLowerInvariant(symbol);

            // «кс» читается как «ks», но игроки говорят «cs»; такие сокращения
            // перечислены отдельно, чтобы не гадать по отдельным буквам.
            if (index + 1 < value.Length)
            {
                var pair = string.Concat(lower, char.ToLowerInvariant(value[index + 1]));
                if (ShortNames.TryGetValue(pair, out var shortName))
                {
                    builder.Append(shortName);
                    index++;
                    continue;
                }
            }

            if (Map.TryGetValue(lower, out var latin))
            {
                builder.Append(char.IsUpper(symbol) ? char.ToUpperInvariant(latin[0]) : latin[0]);
                if (latin.Length > 1)
                {
                    builder.Append(latin[1..]);
                }

                continue;
            }

            builder.Append(symbol);
        }

        return builder.ToString().Trim();
    }

    /// <summary>Устойчивые сокращения, которые иначе транслитерировались бы неверно.</summary>
    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.Ordinal)
    {
        ["кс"] = "cs",
        ["кc"] = "cs"
    };

    private static readonly Dictionary<char, string> Map = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
        ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
    };
}
