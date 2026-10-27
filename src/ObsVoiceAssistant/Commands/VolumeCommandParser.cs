using System.Globalization;
using System.Text;

namespace ObsVoiceAssistant.Commands;

/// <summary>
/// Разбор команд изменения громкости: «громкость музыки плюс 10», «громкость -5».
/// Форма команды фиксированная, но цель и число меняются, поэтому она разбирается
/// отдельно от словаря целых фраз. Имена OBS-источников здесь не участвуют:
/// цель — одна из настроенных ролей.
/// </summary>
public static class VolumeCommandParser
{
    /// <summary>Шаг, если число не названо: «громкость музыки громче».</summary>
    public const int DefaultStep = 10;

    private sealed record TargetForm(string Key, string Text)
    {
        public string[] Tokens { get; } = Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Цели в родительном падеже: «громкость музыки», «громкость микрофона».
    /// Формы с «стримом» проверяются первыми (сортировка по числу слов), иначе
    /// «музыки стрима» превратилось бы в личную музыку.
    /// </summary>
    private static readonly TargetForm[] TargetForms = BuildTargetForms();

    /// <summary>
    /// Точечные замены слов, которые Vosk устойчиво путает в командах громкости.
    /// Это не общий фаззи-матчинг: замена применяется только к указанному каноническому
    /// слову, поэтому не расширяет остальные команды.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RecognitionAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["стрела"] = "стрима"
        };

    private static readonly IReadOnlyDictionary<string, string> NumberAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["стал"] = "сто"
        };

    private static readonly IReadOnlyDictionary<string, int> NumberWords =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ноль"] = 0,
            ["нуль"] = 0,
            ["один"] = 1,
            ["одна"] = 1,
            ["два"] = 2,
            ["две"] = 2,
            ["три"] = 3,
            ["четыре"] = 4,
            ["пять"] = 5,
            ["шесть"] = 6,
            ["семь"] = 7,
            ["восемь"] = 8,
            ["девять"] = 9,
            ["десять"] = 10,
            ["одиннадцать"] = 11,
            ["двенадцать"] = 12,
            ["тринадцать"] = 13,
            ["четырнадцать"] = 14,
            ["пятнадцать"] = 15,
            ["шестнадцать"] = 16,
            ["семнадцать"] = 17,
            ["восемнадцать"] = 18,
            ["девятнадцать"] = 19,
            ["двадцать"] = 20,
            ["тридцать"] = 30,
            ["сорок"] = 40,
            ["пятьдесят"] = 50,
            ["шестьдесят"] = 60,
            ["семьдесят"] = 70,
            ["восемьдесят"] = 80,
            ["девяносто"] = 90,
            ["сто"] = 100
        };

    private static readonly string[] PercentWords =
    {
        "процент",
        "процента",
        "процентов",
        "проценты"
    };

    /// <summary>
    /// Пытается разобрать фразу как команду громкости. Возвращает <c>null</c>,
    /// если фраза не соответствует форме: лучше не выполнить команду, чем
    /// изменить не тот источник или на неверное значение.
    /// </summary>
    public static ParsedCommand? TryParse(
        string recognizedText,
        out IReadOnlyList<TokenCorrection> corrections)
    {
        corrections = Array.Empty<TokenCorrection>();

        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return null;
        }

        var tokens = Prepare(recognizedText)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !CommandVocabulary.Fillers.Contains(token))
            .ToArray();

        if (tokens.Length < 2 || CommandVocabulary.ContainsNegation(string.Join(' ', tokens)))
        {
            return null;
        }

        var preposedSign = 0;
        if (tokens.Length >= 2 &&
            TryGetPreposedSign(tokens[0], out var candidateSign) &&
            MatchesToken(tokens[1], "громкость", null))
        {
            preposedSign = candidateSign;
            tokens = tokens[1..];
        }

        var foundCorrections = new List<TokenCorrection>();
        var index = 0;

        if (!MatchesToken(tokens[index], "громкость", foundCorrections))
        {
            return null;
        }

        index++;

        var target = "microphone";
        var targetForm = FindTarget(tokens, index);
        if (targetForm is not null)
        {
            target = targetForm.Key;
            RecordCorrections(tokens, index, targetForm, foundCorrections);
            index += targetForm.Tokens.Length;
        }

        int sign;
        if (preposedSign != 0)
        {
            // «меньше громкость микрофоном» уже содержит направление.
            // Второе направление в той же фразе не угадываем.
            if (index < tokens.Length &&
                (MatchesToken(tokens[index], "плюс", null) ||
                 MatchesToken(tokens[index], "громче", null) ||
                 MatchesToken(tokens[index], "минус", null) ||
                 MatchesToken(tokens[index], "тише", null)))
            {
                return null;
            }

            sign = preposedSign;
        }
        else
        {
            if (index >= tokens.Length)
            {
                return null;
            }

            if (MatchesToken(tokens[index], "плюс", foundCorrections) ||
                MatchesToken(tokens[index], "громче", foundCorrections))
            {
                sign = 1;
            }
            else if (MatchesToken(tokens[index], "минус", foundCorrections) ||
                     MatchesToken(tokens[index], "тише", foundCorrections))
            {
                sign = -1;
            }
            else
            {
                return null;
            }

            index++;
        }

        var amount = DefaultStep;
        if (index < tokens.Length &&
            TryParseNumber(tokens, index, foundCorrections, out var number, out var consumed))
        {
            if (number > 100)
            {
                return null;
            }

            amount = number;
            index += consumed;
        }

        if (index < tokens.Length && IsPercentWord(tokens[index]))
        {
            index++;
        }

        // Лишние слова не игнорируются: «громкость музыки плюс 10 наверное»
        // не должно менять громкость.
        if (index != tokens.Length)
        {
            return null;
        }

        corrections = foundCorrections;
        return new ParsedCommand(
            CommandIntent.AdjustInputVolume,
            Target: target,
            Delta: sign * amount);
    }

    /// <summary>
    /// Приводит текст к словам, сохраняя знаки «+» и «-»: обычная нормализация
    /// команд выбрасывает их как пунктуацию, а в этой команде они несут смысл.
    /// </summary>
    private static string Prepare(string value)
    {
        var lowered = value.Trim().ToLowerInvariant().Replace('ё', 'е');
        var builder = new StringBuilder(lowered.Length + 8);

        foreach (var symbol in lowered)
        {
            if (symbol == '+')
            {
                builder.Append(" плюс ");
                continue;
            }

            if (symbol == '-' || symbol == '\u2212')
            {
                builder.Append(" минус ");
                continue;
            }

            builder.Append(char.IsLetterOrDigit(symbol) ? symbol : ' ');
        }

        return string.Join(
            ' ',
            builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static TargetForm? FindTarget(string[] tokens, int index)
    {
        foreach (var form in TargetForms)
        {
            if (index + form.Tokens.Length > tokens.Length)
            {
                continue;
            }

            var matches = true;
            for (var offset = 0; offset < form.Tokens.Length; offset++)
            {
                if (!MatchesToken(tokens[index + offset], form.Tokens[offset], null))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return form;
            }
        }

        return null;
    }

    private static void RecordCorrections(
        string[] tokens,
        int index,
        TargetForm form,
        List<TokenCorrection> corrections)
    {
        for (var offset = 0; offset < form.Tokens.Length; offset++)
        {
            var token = tokens[index + offset];
            if (!string.Equals(token, form.Tokens[offset], StringComparison.Ordinal))
            {
                corrections.Add(new TokenCorrection(token, form.Tokens[offset]));
            }
        }
    }

    private static bool TryGetPreposedSign(string token, out int sign)
    {
        switch (token)
        {
            case "меньше":
            case "тише":
            case "потише":
            case "убавь":
            case "уменьши":
                sign = -1;
                return true;

            case "больше":
            case "громче":
            case "погромче":
            case "прибавь":
            case "увеличь":
                sign = 1;
                return true;

            default:
                sign = 0;
                return false;
        }
    }

    private static bool MatchesToken(
        string token,
        string canonical,
        List<TokenCorrection>? corrections)
    {
        if (string.Equals(token, canonical, StringComparison.Ordinal))
        {
            return true;
        }

        if (RecognitionAliases.TryGetValue(token, out var alias) &&
            string.Equals(alias, canonical, StringComparison.Ordinal))
        {
            corrections?.Add(new TokenCorrection(token, canonical));
            return true;
        }

        // Те же пороги, что и у общего парсера: короткие слова не исправляются.
        var maxDistance = token.Length switch
        {
            <= 3 => 0,
            <= 7 => 1,
            _ => 2
        };

        if (maxDistance == 0 || Distance(token, canonical, maxDistance) > maxDistance)
        {
            return false;
        }

        corrections?.Add(new TokenCorrection(token, canonical));
        return true;
    }

    private static bool TryParseNumber(
        string[] tokens,
        int index,
        List<TokenCorrection> corrections,
        out int value,
        out int consumed)
    {
        value = 0;
        consumed = 0;

        if (int.TryParse(tokens[index], NumberStyles.None, CultureInfo.InvariantCulture, out var digits))
        {
            value = digits;
            consumed = 1;
            return true;
        }

        if (!TryResolveNumberWord(tokens[index], corrections, out var first))
        {
            return false;
        }

        if (first >= 20 &&
            first <= 90 &&
            index + 1 < tokens.Length &&
            TryResolveNumberWord(tokens[index + 1], corrections, out var unit) &&
            unit is >= 1 and <= 9)
        {
            value = first + unit;
            consumed = 2;
            return true;
        }

        value = first;
        consumed = 1;
        return true;
    }

    /// <summary>
    /// Понимает число словом, включая ошибку распознавания. Исправление принимается,
    /// только если на минимальном расстоянии оказалось одно число: «десеть» — это 10,
    /// а слово, одинаково близкое к «десять» и «девять», фразу отклоняет.
    /// </summary>
    private static bool TryResolveNumberWord(
        string token,
        List<TokenCorrection> corrections,
        out int value)
    {
        if (NumberWords.TryGetValue(token, out value))
        {
            return true;
        }

        if (NumberAliases.TryGetValue(token, out var alias) &&
            NumberWords.TryGetValue(alias, out value))
        {
            corrections.Add(new TokenCorrection(token, alias));
            return true;
        }

        var maxDistance = token.Length switch
        {
            <= 3 => 0,
            <= 7 => 1,
            _ => 2
        };

        if (maxDistance == 0)
        {
            return false;
        }

        var bestDistance = maxDistance + 1;
        var bestValues = new Dictionary<int, string>();

        foreach (var (word, numberValue) in NumberWords)
        {
            if (Math.Abs(word.Length - token.Length) > maxDistance)
            {
                continue;
            }

            var distance = Distance(token, word, maxDistance);
            if (distance > maxDistance)
            {
                continue;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestValues.Clear();
                bestValues[numberValue] = word;
            }
            else if (distance == bestDistance && !bestValues.ContainsKey(numberValue))
            {
                bestValues[numberValue] = word;
            }
        }

        if (bestValues.Count != 1)
        {
            value = 0;
            return false;
        }

        var winner = bestValues.First();
        value = winner.Key;
        corrections.Add(new TokenCorrection(token, winner.Value));
        return true;
    }

    private static bool IsPercentWord(string token) =>
        PercentWords.Any(word => MatchesToken(token, word, null));

    private static TargetForm[] BuildTargetForms()
    {
        var forms = new List<TargetForm>();

        AddTarget("microphone", "микрофона", "микрофон", "микро");
        AddTarget("browser", "браузера", "браузер");
        AddTarget(
            "personal_music",
            "музыки",
            "музыку",
            "музыка",
            "моей музыки",
            "мою музыку",
            "моя музыка",
            "личной музыки",
            "личную музыку",
            "личная музыка");
        AddTarget(
            "stream_music",
            "стрима",
            "музыки стрима",
            "музыку стрима",
            "музыка стрима",
            "музыки стрим",
            "музыку стрим",
            "музыка стрим",
            "музыки стримов",
            "музыки на стриме",
            "музыку на стриме",
            "музыка на стриме",
            "звука стрима",
            "звук стрима",
            "звука стрим",
            "звук стрим",
            "звука стримов",
            "звука на стриме",
            "звук на стриме");

        return forms
            .OrderByDescending(form => form.Tokens.Length)
            .ThenByDescending(form => form.Text.Length)
            .ToArray();

        void AddTarget(string key, params string[] texts)
        {
            foreach (var text in texts)
            {
                forms.Add(new TargetForm(key, text));
            }
        }
    }

    private static int Distance(string left, string right, int limit)
    {
        if (Math.Abs(left.Length - right.Length) > limit)
        {
            return limit + 1;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var index = 0; index <= right.Length; index++)
        {
            previous[index] = index;
        }

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            var rowMinimum = current[0];

            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var cost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[rightIndex]);
            }

            if (rowMinimum > limit)
            {
                return limit + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
