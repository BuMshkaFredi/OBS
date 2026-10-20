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
        if (command is not null)
        {
            return new ParseResult(command, matchedText, corrections);
        }

        // 4. Исправление фразы целиком: Vosk часто переставляет или сливает слова,
        // поэтому посимвольное расстояние надёжнее правки одного слова.
        var fuzzy = MatchFuzzy(withoutFillers);
        if (fuzzy is not null)
        {
            return new ParseResult(
                fuzzy.Value.Command,
                fuzzy.Value.Phrase,
                new[] { new TokenCorrection(withoutFillers, fuzzy.Value.Phrase) });
        }

        return ParseResult.NotFound(text);
    }

    /// <summary>
    /// Подсказывает ближайшую словарную фразу для лога. Нужна, чтобы добавлять
    /// фактические формулировки в словарь, а не гадать.
    /// </summary>
    public PhraseSuggestion? SuggestClosest(string recognizedText)
    {
        var text = CommandVocabulary.Normalize(recognizedText);
        // Для коротких слов и отрицаний «ближайшая фраза» почти всегда случайна:
        // «не» не должно подсказывать «мут», а «мод» — добавлять себя в словарь.
        if (text.Length < 4 || CommandVocabulary.ContainsNegation(text))
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
    /// Подбирает ближайшую команду по расстоянию всей фразы. Это ловит ошибки,
    /// которые не исправляются по одному слову: «нашли запись», «начни с три»,
    /// «основании запись». Дальние догадки ограничены: до трёх правок на всю
    /// фразу, а больше — только когда расходится первый глагол, а остальное
    /// совпало точно. Так «включи музыку потише» не превращается в команду.
    /// </summary>
    private (ParsedCommand Command, string Phrase)? MatchFuzzy(string text)
    {
        // Совсем короткие фразы и фразы без единого канонического слова
        // не додумываем: это почти всегда фоновая речь.
        if (text.Length < 8 || !ContainsCanonicalToken(text))
        {
            return null;
        }

        const int searchLimit = 5;
        var inputFirst = GetFirstToken(text);
        var bestDistance = int.MaxValue;
        var bestPhrases = new List<string>();
        var bestCommands = new Dictionary<string, ParsedCommand>(StringComparer.Ordinal);

        foreach (var (phraseText, phrase) in phraseLookup)
        {
            // «Включи весь звук» не должно становиться «выключи весь звук»
            // из-за одной ошибки распознавания: направление команды важнее.
            if (CommandVocabulary.AreOppositeVerbs(inputFirst, GetFirstToken(phraseText)))
            {
                continue;
            }

            var distance = Distance(text, phraseText, searchLimit);
            if (distance > searchLimit)
            {
                continue;
            }

            var command = new ParsedCommand(phrase.Intent, Target: phrase.Target);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestPhrases.Clear();
                bestPhrases.Add(phraseText);
                bestCommands.Clear();
                bestCommands[Identify(command)] = command;
            }
            else if (distance == bestDistance)
            {
                bestPhrases.Add(phraseText);
                bestCommands[Identify(command)] = command;
            }
        }

        // Разные команды на одном расстоянии — не угадываем.
        if (bestCommands.Count != 1)
        {
            return null;
        }

        // Лишнее слово нельзя просто «списать» в расстояние: «включи браузер же»
        // и «на запусти стрим» должны остаться нераспознанными. Совпадение по
        // числу слов или объяснимое слияние/разделение — обязательное условие.
        var matchedPhrase = bestPhrases.FirstOrDefault(phrase => TokenCountsAreCompatible(text, phrase));
        if (matchedPhrase is null)
        {
            return null;
        }

        var accepted = bestDistance <= 3 ||
            (bestDistance <= searchLimit && IsFirstTokenOnlyMismatch(text, matchedPhrase));

        return accepted
            ? (bestCommands.Values.First(), matchedPhrase)
            : null;
    }

    /// <summary>
    /// Проверяет, что различие в числе слов объясняется слиянием или разделением
    /// соседних слов, а не потерянным или добавленным словом.
    /// </summary>
    private static bool TokenCountsAreCompatible(string text, string phraseText)
    {
        var input = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var phrase = phraseText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (input.Length == phrase.Length)
        {
            return TokensAreSimilarEnough(input, phrase);
        }

        if (input.Length == phrase.Length + 1)
        {
            return CanMergeAdjacentInputTokens(input, phrase);
        }

        if (input.Length + 1 == phrase.Length)
        {
            return CanSplitPhraseToken(input, phrase);
        }

        return false;
    }

    /// <summary>
    /// При одинаковом числе слов проверяет, что изменённые слова действительно
    /// похожи. Иначе «выключи окно» превратилось бы в «выключи микро», а
    /// «это запись» — в «стоп запись».
    /// </summary>
    private static bool TokensAreSimilarEnough(string[] input, string[] phrase)
    {
        for (var index = 0; index < input.Length; index++)
        {
            if (string.Equals(input[index], phrase[index], StringComparison.Ordinal))
            {
                continue;
            }

            // Первое слово — глагол. Короткие служебные слова («это», «вот»,
            // «наш») не должны становиться командой только из-за расстояния.
            // Исключение — опечатка в одну букву: «стп стрим» → «стоп стрим».
            if (index == 0)
            {
                if (input[index].Length < 5 &&
                    Distance(input[index], phrase[index], 1) > 1)
                {
                    return false;
                }

                continue;
            }

            // Для остальных слов требуем общего фрагмента из трёх букв:
            // «рим»→«стрим», «записи»→«запись». «Эти»→«стоп» так не пройдёт.
            if (!HasCommonSubstringOfLength(input[index], phrase[index], 3))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Есть ли у строк общий непрерывный фрагмент нужной длины.</summary>
    private static bool HasCommonSubstringOfLength(string left, string right, int length)
    {
        if (left.Length < length || right.Length < length)
        {
            return false;
        }

        for (var start = 0; start <= left.Length - length; start++)
        {
            if (right.Contains(left.Substring(start, length), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>«с три» → «стрим»: два коротких слова слились в одно.</summary>
    private static bool CanMergeAdjacentInputTokens(string[] input, string[] phrase)
    {
        for (var index = 0; index < input.Length - 1; index++)
        {
            if (Math.Min(input[index].Length, input[index + 1].Length) > 2)
            {
                continue;
            }

            if (Distance(input[index] + input[index + 1], phrase[index], 1) > 1)
            {
                continue;
            }

            var matches = true;
            for (var position = 0; position < phrase.Length; position++)
            {
                if (position == index)
                {
                    continue;
                }

                var inputPosition = position < index ? position : position + 1;
                if (!string.Equals(input[inputPosition], phrase[position], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>«навстречу» → «на стриме»: одно слово распалось на два.</summary>
    private static bool CanSplitPhraseToken(string[] input, string[] phrase)
    {
        for (var index = 0; index < phrase.Length - 1; index++)
        {
            var joined = phrase[index] + phrase[index + 1];
            if (Distance(input[index], joined, 4) > 4)
            {
                continue;
            }

            var matches = true;
            for (var position = 0; position < input.Length; position++)
            {
                if (position == index)
                {
                    continue;
                }

                var phrasePosition = position < index ? position : position + 1;
                if (!string.Equals(input[position], phrase[phrasePosition], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Проверяет, что все слова, кроме первого, совпали точно. Первое слово —
    /// глагол: «основании запись» может отличаться от «останови запись» сильнее,
    /// чем допускает правка одного слова, но цель уже однозначна.
    /// </summary>
    private static bool IsFirstTokenOnlyMismatch(string text, string phraseText)
    {
        var input = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var phrase = phraseText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (input.Length != phrase.Length || input.Length < 2 || input[0].Length < 5)
        {
            return false;
        }

        for (var index = 1; index < input.Length; index++)
        {
            if (!string.Equals(input[index], phrase[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Первое слово фразы: обычно это глагол команды.</summary>
    private static string GetFirstToken(string text)
    {
        var space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }

    /// <summary>Есть ли во фразе слово, которое уже точно есть в словаре команд.</summary>
    private static bool ContainsCanonicalToken(string text) =>
        text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(CommandVocabulary.Tokens.Contains);

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
