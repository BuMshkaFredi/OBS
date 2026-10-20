namespace ObsVoiceAssistant.Commands;

/// <summary>Исправление одного слова: что услышали и на что заменили.</summary>
public sealed record TokenCorrection(string From, string To);

/// <summary>Ближайшая словарная фраза: подсказка для расширения словаря.</summary>
public sealed record PhraseSuggestion(CommandVocabulary.CommandPhrase Phrase, int Distance);

/// <summary>Результат разбора: команда, нормализованный текст и исправления слов.</summary>
public sealed record ParseResult(
    ParsedCommand? Command,
    string NormalizedText,
    IReadOnlyList<TokenCorrection> Corrections)
{
    public bool Recognized => Command is not null;

    public static ParseResult NotFound(string normalizedText) =>
        new(null, normalizedText, Array.Empty<TokenCorrection>());
}

/// <summary>
/// Разбор распознанной фразы. Фраза должна совпасть со словарной целиком.
/// Допускается исправление отдельных слов по словарю канонических слов, но решение
/// принимается по смыслу: если все варианты исправления дают одну и ту же команду,
/// она выполняется; если разные — команда не выполняется.
/// </summary>
public sealed class CommandParser
{
    /// <summary>Предохранитель от комбинаторного перебора вариантов исправления.</summary>
    private const int MaxCorrectionCombinations = 256;

    private readonly Dictionary<string, CommandVocabulary.CommandPhrase> phraseLookup;

    public CommandParser()
        : this(CommandVocabulary.Phrases)
    {
    }

    public CommandParser(IEnumerable<CommandVocabulary.CommandPhrase> phrases)
    {
        phraseLookup = new Dictionary<string, CommandVocabulary.CommandPhrase>(StringComparer.Ordinal);
        foreach (var phrase in phrases)
        {
            phraseLookup[CommandVocabulary.Normalize(phrase.Phrase)] = phrase;
        }
    }

    public ParsedCommand? Parse(string recognizedText) => ParseWithDetails(recognizedText).Command;

    public ParseResult ParseWithDetails(string recognizedText)
    {
        var text = CommandVocabulary.Normalize(recognizedText);
        if (text.Length == 0)
        {
            return ParseResult.NotFound(text);
        }

        // Отрицание переворачивает смысл: «не начни запись» не должно стать «начни запись».
        if (CommandVocabulary.ContainsNegation(text))
        {
            return ParseResult.NotFound(text);
        }

        // 1. Точное совпадение — основной и самый безопасный путь.
        var exact = Match(text, out _);
        if (exact is not null)
        {
            return new ParseResult(exact, text, Array.Empty<TokenCorrection>());
        }

        // 2. Без слов-заполнителей: «начни запись пожалуйста».
        var withoutFillers = CommandVocabulary.RemoveFillers(text);
        if (!string.Equals(withoutFillers, text, StringComparison.Ordinal))
        {
            var stripped = Match(withoutFillers, out _);
            if (stripped is not null)
            {
                return new ParseResult(stripped, withoutFillers, Array.Empty<TokenCorrection>());
            }
        }

        // 3. Исправление слов с проверкой смысла получившейся команды.
        var corrections = new List<TokenCorrection>();
        var (command, matchedText) = MatchCorrected(withoutFillers, corrections);
        return command is not null
            ? new ParseResult(command, matchedText, corrections)
            : ParseResult.NotFound(text);
    }

    /// <summary>
    /// Подсказывает ближайшую словарную фразу для лога. Нужна, чтобы добавлять
    /// фактические формулировки в словарь, а не гадать.
    /// </summary>
    public PhraseSuggestion? SuggestClosest(string recognizedText)
    {
        var text = CommandVocabulary.Normalize(recognizedText);
        if (text.Length == 0)
        {
            return null;
        }

        var limit = Math.Max(4, text.Length / 3);
        CommandVocabulary.CommandPhrase? best = null;
        var bestDistance = int.MaxValue;
        var bestText = string.Empty;

        foreach (var (phraseText, phrase) in phraseLookup)
        {
            var distance = Distance(text, phraseText, limit);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = phrase;
                bestText = phraseText;
            }
        }

        return best is not null && bestDistance <= limit
            ? new PhraseSuggestion(new CommandVocabulary.CommandPhrase(bestText, best.Intent, best.Target), bestDistance)
            : null;
    }

    private ParsedCommand? Match(string text, out int prefixWords)
    {
        prefixWords = 0;

        if (text.Length == 0)
        {
            return null;
        }

        if (phraseLookup.TryGetValue(text, out var phrase))
        {
            return new ParsedCommand(phrase.Intent, Target: phrase.Target);
        }

        foreach (var argumentCommand in CommandVocabulary.ArgumentCommands)
        {
            var prefix = CommandVocabulary.Normalize(argumentCommand.Prefix);
            if (text.Length > prefix.Length &&
                text.StartsWith(prefix, StringComparison.Ordinal) &&
                text[prefix.Length] == ' ')
            {
                var argument = text[(prefix.Length + 1)..].Trim();
                if (argument.Length > 0)
                {
                    prefixWords = prefix.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    return new ParsedCommand(argumentCommand.Intent, Argument: argument);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Перебирает допустимые исправления слов и принимает решение по команде, а не по слову:
    /// «запису» одинаково близко к «записи» и «запись», но команда в обоих случаях одна.
    /// </summary>
    private (ParsedCommand? Command, string MatchedText) MatchCorrected(
        string text,
        List<TokenCorrection> corrections)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return (null, string.Empty);
        }

        var candidateSets = new List<string>[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            candidateSets[index] = CandidateTokens(tokens[index]);
        }

        // Аргумент (название стрима, категория) — пользовательский текст.
        // Он не исправляется: иначе в OBS уйдёт другое название.
        var argumentStart = FindArgumentStart(tokens, candidateSets);
        for (var index = argumentStart; index < tokens.Length; index++)
        {
            candidateSets[index] = new List<string> { tokens[index] };
        }

        long combinations = 1;
        foreach (var candidateSet in candidateSets)
        {
            combinations *= candidateSet.Count;
            if (combinations > MaxCorrectionCombinations)
            {
                return (null, string.Empty);
            }
        }

        var commands = new Dictionary<string, ParsedCommand>(StringComparer.Ordinal);
        string? matchedText = null;
        var current = new string[tokens.Length];

        void Explore(int position)
        {
            if (position == candidateSets.Length)
            {
                var candidateText = string.Join(' ', current);
                var command = Match(candidateText, out _);
                if (command is not null)
                {
                    commands[Identify(command)] = command;
                    matchedText ??= candidateText;
                }

                return;
            }

            foreach (var candidate in candidateSets[position])
            {
                current[position] = candidate;
                Explore(position + 1);
            }
        }

        Explore(0);

        // Разные трактовки означают разные команды — угадывать нельзя.
        if (commands.Count != 1 || matchedText is null)
        {
            return (null, string.Empty);
        }

        var winner = matchedText.Split(' ');
        for (var index = 0; index < tokens.Length; index++)
        {
            if (!string.Equals(tokens[index], winner[index], StringComparison.Ordinal))
            {
                corrections.Add(new TokenCorrection(tokens[index], winner[index]));
            }
        }

        return (commands.Values.First(), matchedText);
    }

    /// <summary>
    /// Находит начало аргумента — позицию после префикса команды с аргументом.
    /// Префикс тоже может быть распознан с ошибками, поэтому проверяются его варианты.
    /// </summary>
    private static int FindArgumentStart(string[] tokens, List<string>[] candidates)
    {
        var start = tokens.Length;

        foreach (var argumentCommand in CommandVocabulary.ArgumentCommands)
        {
            var prefixTokens = CommandVocabulary
                .Normalize(argumentCommand.Prefix)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length <= prefixTokens.Length)
            {
                continue;
            }

            var matches = true;
            for (var index = 0; index < prefixTokens.Length; index++)
            {
                if (candidates[index].Contains(prefixTokens[index]))
                {
                    continue;
                }

                matches = false;
                break;
            }

            if (matches && prefixTokens.Length < start)
            {
                start = prefixTokens.Length;
            }
        }

        return start;
    }

    /// <summary>
    /// Все канонические слова на минимальном расстоянии от распознанного.
    /// Слово без подходящих вариантов возвращается как есть.
    /// </summary>
    private static List<string> CandidateTokens(string token)
    {
        if (CommandVocabulary.Tokens.Contains(token))
        {
            return new List<string> { token };
        }

        // Короткие слова править нельзя: «не» и «на» отличаются одной буквой.
        var maxDistance = token.Length switch
        {
            <= 3 => 0,
            <= 7 => 1,
            _ => 2
        };

        if (maxDistance == 0)
        {
            return new List<string> { token };
        }

        var bestDistance = maxDistance + 1;
        var candidates = new List<string>();

        foreach (var candidate in CommandVocabulary.Tokens)
        {
            if (Math.Abs(candidate.Length - token.Length) > maxDistance)
            {
                continue;
            }

            var distance = Distance(token, candidate, maxDistance);
            if (distance > maxDistance)
            {
                continue;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                candidates.Clear();
                candidates.Add(candidate);
            }
            else if (distance == bestDistance)
            {
                candidates.Add(candidate);
            }
        }

        return candidates.Count == 0 ? new List<string> { token } : candidates;
    }

    private static string Identify(ParsedCommand command) =>
        $"{command.Intent}|{command.Target}|{command.Argument}";

    /// <summary>Расстояние Левенштейна с ранним выходом: дальше <paramref name="limit"/> не считаем.</summary>
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

                if (current[rightIndex] < rowMinimum)
                {
                    rowMinimum = current[rightIndex];
                }
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
