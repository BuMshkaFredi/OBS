using ObsVoiceAssistant.Commands;

namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>
/// Русские названия популярных категорий Twitch, которые транслитерация не находит.
/// «Пас оф экзайл» даёт «pas of ekzayl», а Twitch ищет «Path of Exile», поэтому
/// для таких игр хранится явная таблица произносимых форм. Таблица детерминированная,
/// без сети: она только подсказывает, по каким латинским названиям искать категорию.
/// </summary>
public static class TwitchCategoryAliases
{
    /// <summary>Произносимые формы и названия для поиска на Twitch в порядке приоритета.</summary>
    public sealed record TwitchCategoryAlias(
        IReadOnlyList<string> SpokenNames,
        IReadOnlyList<string> SearchNames);

    /// <summary>
    /// Таблица составляется по реальным формулировкам, а не по догадкам. Новые игры
    /// добавляются сюда, если транслитерация не находит категорию.
    /// </summary>
    public static readonly IReadOnlyList<TwitchCategoryAlias> All = new[]
    {
        new TwitchCategoryAlias(
            SpokenNames: new[]
            {
                "пое", "поэ", "пас оф экзайл", "пас оф экзаил", "паф оф экзайл", "по оф экзайл",
                // Лог клиента 31.10.2026: Vosk слышит «пас оф экзайл» как «пасов
                // экзамен», а официальное русское название игры — «Путь изгнанника».
                "пасов экзамен", "пассов экзамен", "пасов экзамена", "пас о экзамен",
                "пас оф экзамен", "путь изгнанника", "напои", "напое"
            },
            SearchNames: new[] { "Path of Exile" }),

        new TwitchCategoryAlias(
            SpokenNames: new[] { "пое 2", "поэ 2", "пас оф экзайл 2", "пас оф экзаил 2", "паф оф экзайл 2" },
            SearchNames: new[] { "Path of Exile 2" }),

        // «Дота» и «дота два» уже находятся транслитерацией, но винительный падеж
        // «на доту» даёт «dotu», а Vosk слышит «дота два» как «дота дс»/«дота дпс».
        new TwitchCategoryAlias(
            SpokenNames: new[]
            {
                "доту", "доты", "доте", "дота дс", "доту дс", "доты дс", "дота дпс", "доту дпс"
            },
            SearchNames: new[] { "Dota 2" }),

        new TwitchCategoryAlias(
            SpokenNames: new[] { "майнкрафт", "майн крафт", "майн" },
            SearchNames: new[] { "Minecraft" }),

        // Текущая категория Twitch называется «The Binding of Isaac: Repentance»;
        // поиск по короткому названию возвращает её первым результатом.
        new TwitchCategoryAlias(
            SpokenNames: new[] { "айзек", "айзэк", "айзек репенденс", "айзэк репенденс" },
            SearchNames: new[] { "The Binding of Isaac", "The Binding of Isaac: Repentance" }),

        new TwitchCategoryAlias(
            SpokenNames: new[] { "раст", "руст" },
            SearchNames: new[] { "Rust" }),

        new TwitchCategoryAlias(
            SpokenNames: new[] { "торчлайт инфинити", "торч лайт инфинити", "торчлайт" },
            SearchNames: new[] { "Torchlight: Infinite", "Torchlight Infinite" }),

        new TwitchCategoryAlias(
            SpokenNames: new[] { "джаст чатинг", "джаст чаттинг", "джастчатинг", "джаст чат", "джаст" },
            SearchNames: new[] { "Just Chatting" })
    };

    /// <summary>
    /// Ищет категорию по нормализованной фразе. Сначала точное совпадение, затем
    /// ограниченная правка опечатки распознавания. Короткие названия (до 4 букв)
    /// не исправляются, а числовые формы («пое 3») не подбираются приблизительно:
    /// иначе «пое три» стало бы «Path of Exile 2». Неоднозначность не угадывается.
    /// </summary>
    public static bool TryResolve(string? normalizedSpoken, out IReadOnlyList<string> searchNames)
    {
        searchNames = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(normalizedSpoken))
        {
            return false;
        }

        var value = CommandVocabulary.Normalize(normalizedSpoken);
        if (value.Length == 0)
        {
            return false;
        }

        var exact = All
            .Where(alias => alias.SpokenNames.Contains(value, StringComparer.Ordinal))
            .ToArray();
        if (exact.Length == 1)
        {
            searchNames = exact[0].SearchNames;
            return true;
        }

        if (exact.Length > 1 || value.Any(char.IsDigit))
        {
            return false;
        }

        var fuzzy = All
            .Where(alias => alias.SpokenNames.Any(spoken => IsCloseEnough(value, spoken)))
            .ToArray();
        if (fuzzy.Length != 1)
        {
            return false;
        }

        searchNames = fuzzy[0].SearchNames;
        return true;
    }

    /// <summary>
    /// Допуск на опечатку как в словаре команд: 5–7 букв — одна правка, 8 и больше — две,
    /// более короткие слова исправляются только точным совпадением.
    /// </summary>
    private static bool IsCloseEnough(string value, string spoken)
    {
        if (spoken.Any(char.IsDigit))
        {
            return false;
        }

        var limit = spoken.Length >= 8 ? 2 : spoken.Length >= 5 ? 1 : 0;
        return limit > 0 && Distance(value, spoken) <= limit;
    }

    /// <summary>Расстояние Левенштейна: таблица маленькая, поэтому без оптимизаций.</summary>
    private static int Distance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
