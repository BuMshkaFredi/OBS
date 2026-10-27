using System.Text;

namespace ObsVoiceAssistant.Commands;

/// <summary>
/// Единый источник текстов голосовых команд. Помимо точных фраз здесь хранится
/// словарь канонических слов: по нему исправляются ошибки распознавания в 1–3 буквы.
/// </summary>
public static class CommandVocabulary
{
    /// <summary>Фраза команды и её смысл. <see cref="CommandPhrase.Target"/> нужен для mute/unmute.</summary>
    public sealed record CommandPhrase(string Phrase, CommandIntent Intent, string? Target = null);

    /// <summary>Префиксные команды с аргументом.</summary>
    public sealed record ArgumentCommand(string Prefix, CommandIntent Intent);

    /// <summary>
    /// Слова-заполнители. Они не несут смысла команды и удаляются перед сопоставлением,
    /// чтобы «начни запись пожалуйста» работало так же, как «начни запись».
    /// </summary>
    public static readonly IReadOnlySet<string> Fillers = new HashSet<string>(StringComparer.Ordinal)
    {
        "пожалуйста",
        "пожалуста",
        "плиз",
        "пж",
        "давай",
        "ну",
        "ка",
        "мне",
        "надо",
        "нужно",
        "сейчас",
        "быстро",
        "быстренько",
        "побыстрее",
        "побыстрому"
    };

    /// <summary>
    /// Отрицания. Фраза с отрицанием не разбирается целиком: «не начни запись»
    /// не должно превращаться в «начни запись».
    /// </summary>
    public static readonly IReadOnlySet<string> Negations = new HashSet<string>(StringComparer.Ordinal)
    {
        "не",
        "ни",
        "нельзя"
    };

    /// <summary>
    /// Цель звуковой команды. <see cref="ScopeForms"/> — уточнения источника
    /// («стрима», «на стриме»). Если они заданы, форма без уточнения не генерируется:
    /// «выключи музыку» должно остаться личной музыкой, а не музыкой стрима.
    /// </summary>
    private sealed record AudioTarget(
        string Key,
        string Name,
        bool Reverse,
        CommandIntent OffIntent,
        CommandIntent OnIntent,
        IReadOnlyList<string>? ScopeForms = null);

    /// <summary>
    /// Формы слова «стрим» в музыке стрима, включая предлог «на». Перечислены явно,
    /// потому что «стрим» уже каноническое слово трансляции и исправляться не должно.
    /// Vosk слышит «стрима», «стрим», «стримов», «на стриме», «на стрима» и «на стремя»;
    /// с предлогом «на» фраза иначе не совпадает целиком.
    /// </summary>
    private static readonly string[] StreamScopeForms =
    {
        "стрима",
        "стрим",
        "стримов",
        "стремя",
        "на стриме",
        "на стрим",
        "на стрима",
        "на стремя",
        "на стримов"
    };

    /// <summary>
    /// Цели звуковых команд: код настройки, слово в речи и то, что означают для неё
    /// глаголы «выключить» и «включить». Микрофон и личная музыка переключаются
    /// любой из формулировок, браузер и музыка стрима понимают их буквально.
    /// Слово дано в винительном падеже: «выключи музыку», «музыку выключи».
    /// </summary>
    private static readonly AudioTarget[] AudioTargets =
    {
        new("microphone", "микрофон", true, CommandIntent.ToggleInput, CommandIntent.ToggleInput),
        new("microphone", "микро", false, CommandIntent.ToggleInput, CommandIntent.ToggleInput),
        new("browser", "браузер", true, CommandIntent.MuteInput, CommandIntent.UnmuteInput),

        // Личная музыка: «выключи музыку» — такое же переключение, как у микрофона.
        new("personal_music", "музыку", true, CommandIntent.ToggleInput, CommandIntent.ToggleInput),
        new("personal_music", "мою музыку", true, CommandIntent.ToggleInput, CommandIntent.ToggleInput),
        new("personal_music", "личную музыку", true, CommandIntent.ToggleInput, CommandIntent.ToggleInput),

        // Музыка стрима: только явные mute/unmute. Слово «стрим» обязательно,
        // иначе «выключи стрим» осталось бы остановкой трансляции.
        new("stream_music", "музыку", true, CommandIntent.MuteInput, CommandIntent.UnmuteInput, StreamScopeForms),
        new("stream_music", "звук", true, CommandIntent.MuteInput, CommandIntent.UnmuteInput, StreamScopeForms)
    };

    /// <summary>Глаголы «выключить». Работают с любой целью из <see cref="AudioTargets"/>.</summary>
    private static readonly string[] OffVerbs =
    {
        "выключи", "выключить", "выключай", "заглуши", "заглушить", "замьють",
        "замьютить", "замуть", "убери", "выруби", "вырубай", "отключи", "отключить", "мут"
    };

    /// <summary>Глаголы «включить».</summary>
    private static readonly string[] OnVerbs =
    {
        "включи", "включить", "включай", "верни", "вернуть", "размут", "размуть",
        "размьють", "вруби", "врубай", "подключи", "подключить"
    };

    /// <summary>
    /// Явные формулировки, которые не сводятся к схеме «глагол + источник».
    /// Систематические формы добавляются в <see cref="Phrases"/>.
    /// </summary>
    private static readonly CommandPhrase[] ExplicitPhrases = new CommandPhrase[]
    {
        // Запись: старт.
        new("начни запись", CommandIntent.StartRecording),
        new("начать запись", CommandIntent.StartRecording),
        new("начинай запись", CommandIntent.StartRecording),
        new("начинаем запись", CommandIntent.StartRecording),
        new("начнем запись", CommandIntent.StartRecording),
        new("начну запись", CommandIntent.StartRecording),
        new("запусти запись", CommandIntent.StartRecording),
        new("запустить запись", CommandIntent.StartRecording),
        new("запускай запись", CommandIntent.StartRecording),
        new("включи запись", CommandIntent.StartRecording),
        new("включить запись", CommandIntent.StartRecording),
        new("пусти запись", CommandIntent.StartRecording),
        new("пустить запись", CommandIntent.StartRecording),
        new("старт записи", CommandIntent.StartRecording),
        new("старт запись", CommandIntent.StartRecording),
        new("стартуй запись", CommandIntent.StartRecording),
        new("вруби запись", CommandIntent.StartRecording),
        new("врубай запись", CommandIntent.StartRecording),
        new("пиши запись", CommandIntent.StartRecording),
        new("запись начни", CommandIntent.StartRecording),
        new("запись запусти", CommandIntent.StartRecording),
        new("запись старт", CommandIntent.StartRecording),
        new("запись включай", CommandIntent.StartRecording),
        new("запись начать", CommandIntent.StartRecording),
        new("запись запустить", CommandIntent.StartRecording),

        // Запись: стоп.
        new("останови запись", CommandIntent.StopRecording),
        new("остановить запись", CommandIntent.StopRecording),
        new("останавливай запись", CommandIntent.StopRecording),
        new("остановка записи", CommandIntent.StopRecording),
        new("остановку записи", CommandIntent.StopRecording),
        new("прекрати запись", CommandIntent.StopRecording),
        new("прекратить запись", CommandIntent.StopRecording),
        new("прекращай запись", CommandIntent.StopRecording),
        new("закончи запись", CommandIntent.StopRecording),
        new("закончить запись", CommandIntent.StopRecording),
        new("заканчивай запись", CommandIntent.StopRecording),
        new("заверши запись", CommandIntent.StopRecording),
        new("завершить запись", CommandIntent.StopRecording),
        new("завершай запись", CommandIntent.StopRecording),
        new("выключи запись", CommandIntent.StopRecording),
        new("выключить запись", CommandIntent.StopRecording),
        new("стоп запись", CommandIntent.StopRecording),
        new("запись стоп", CommandIntent.StopRecording),
        new("запись останови", CommandIntent.StopRecording),
        new("запись прекрати", CommandIntent.StopRecording),
        new("запись закончи", CommandIntent.StopRecording),
        new("запись заверши", CommandIntent.StopRecording),
        new("запись выключи", CommandIntent.StopRecording),
        new("запись выключить", CommandIntent.StopRecording),
        new("запись остановить", CommandIntent.StopRecording),
        new("запись прекратить", CommandIntent.StopRecording),
        new("запись закончить", CommandIntent.StopRecording),
        new("запись завершить", CommandIntent.StopRecording),
        new("стоп записи", CommandIntent.StopRecording),
        new("конец записи", CommandIntent.StopRecording),
        new("сохрани запись", CommandIntent.StopRecording),
        new("сохранить запись", CommandIntent.StopRecording),
        new("запись сохрани", CommandIntent.StopRecording),

        // Стрим: старт.
        new("начни стрим", CommandIntent.StartStream),
        // Vosk слышит «начни стрим» как «начни стареем»; формулировка взята из лога.
        new("начни стареем", CommandIntent.StartStream),
        // Vosk слышит «начни» как «ночи» (лог 21.10 2026, 22:04).
        new("ночи стрим", CommandIntent.StartStream),
        new("начать стрим", CommandIntent.StartStream),
        new("начинай стрим", CommandIntent.StartStream),
        new("начинаем стрим", CommandIntent.StartStream),
        new("начнем стрим", CommandIntent.StartStream),
        new("запусти стрим", CommandIntent.StartStream),
        new("запустить стрим", CommandIntent.StartStream),
        new("запускай стрим", CommandIntent.StartStream),
        new("включи стрим", CommandIntent.StartStream),
        new("включить стрим", CommandIntent.StartStream),
        new("старт стрима", CommandIntent.StartStream),
        new("старт стрим", CommandIntent.StartStream),
        new("стартуй стрим", CommandIntent.StartStream),
        new("вруби стрим", CommandIntent.StartStream),
        new("стрим начни", CommandIntent.StartStream),
        new("стрим запусти", CommandIntent.StartStream),
        new("стрим старт", CommandIntent.StartStream),
        new("начни трансляцию", CommandIntent.StartStream),
        new("начать трансляцию", CommandIntent.StartStream),
        new("начинай трансляцию", CommandIntent.StartStream),
        new("запусти трансляцию", CommandIntent.StartStream),
        new("запускай трансляцию", CommandIntent.StartStream),
        new("включи трансляцию", CommandIntent.StartStream),
        new("включить трансляцию", CommandIntent.StartStream),
        new("старт трансляции", CommandIntent.StartStream),
        new("трансляцию начни", CommandIntent.StartStream),
        new("стрим начать", CommandIntent.StartStream),
        new("стрим запустить", CommandIntent.StartStream),
        new("трансляцию начать", CommandIntent.StartStream),
        new("трансляцию запустить", CommandIntent.StartStream),
        new("трансляцию старт", CommandIntent.StartStream),

        // Стрим: стоп.
        new("останови стрим", CommandIntent.StopStream),
        new("остановить стрим", CommandIntent.StopStream),
        new("останавливай стрим", CommandIntent.StopStream),
        new("прекрати стрим", CommandIntent.StopStream),
        new("прекратить стрим", CommandIntent.StopStream),
        new("прекращай стрим", CommandIntent.StopStream),
        new("закончи стрим", CommandIntent.StopStream),
        new("закончить стрим", CommandIntent.StopStream),
        new("заверши стрим", CommandIntent.StopStream),
        new("завершить стрим", CommandIntent.StopStream),
        new("завершай стрим", CommandIntent.StopStream),
        new("выключи стрим", CommandIntent.StopStream),
        new("выключить стрим", CommandIntent.StopStream),
        new("стоп стрим", CommandIntent.StopStream),
        new("стрим стоп", CommandIntent.StopStream),
        new("стрим останови", CommandIntent.StopStream),
        new("стрим прекрати", CommandIntent.StopStream),
        new("стрим закончи", CommandIntent.StopStream),
        new("стрим выключи", CommandIntent.StopStream),
        new("останови трансляцию", CommandIntent.StopStream),
        new("остановить трансляцию", CommandIntent.StopStream),
        new("прекрати трансляцию", CommandIntent.StopStream),
        new("прекратить трансляцию", CommandIntent.StopStream),
        new("закончи трансляцию", CommandIntent.StopStream),
        new("заверши трансляцию", CommandIntent.StopStream),
        new("выключи трансляцию", CommandIntent.StopStream),
        new("выключить трансляцию", CommandIntent.StopStream),
        new("стоп трансляция", CommandIntent.StopStream),
        new("трансляцию останови", CommandIntent.StopStream),
        new("трансляцию прекрати", CommandIntent.StopStream),
        new("стрим остановить", CommandIntent.StopStream),
        new("стрим прекратить", CommandIntent.StopStream),
        new("стрим закончить", CommandIntent.StopStream),
        new("стрим завершить", CommandIntent.StopStream),
        new("трансляцию остановить", CommandIntent.StopStream),
        new("трансляцию прекратить", CommandIntent.StopStream),
        new("трансляцию закончить", CommandIntent.StopStream),
        new("трансляцию завершить", CommandIntent.StopStream),
        new("стоп стрима", CommandIntent.StopStream),
        new("стоп трансляции", CommandIntent.StopStream),
        new("остановка стрима", CommandIntent.StopStream),
        new("остановка трансляции", CommandIntent.StopStream),
        new("конец стрима", CommandIntent.StopStream),
        new("конец трансляции", CommandIntent.StopStream),

        // Replay Buffer: сохранить повтор.
        new("сохрани повтор", CommandIntent.SaveReplay),
        new("сохраним повтор", CommandIntent.SaveReplay),
        new("сохранить повтор", CommandIntent.SaveReplay),
        new("сохраняй повтор", CommandIntent.SaveReplay),
        new("сохрани повторы", CommandIntent.SaveReplay),
        new("сохранить повторы", CommandIntent.SaveReplay),
        new("сохрани повторчик", CommandIntent.SaveReplay),
        new("сохрани реплей", CommandIntent.SaveReplay),
        new("сохранить реплей", CommandIntent.SaveReplay),
        new("сохрани клип", CommandIntent.SaveReplay),
        new("сохранить клип", CommandIntent.SaveReplay),
        new("сделай повтор", CommandIntent.SaveReplay),
        new("сделать повтор", CommandIntent.SaveReplay),
        new("запиши повтор", CommandIntent.SaveReplay),
        new("запиши реплей", CommandIntent.SaveReplay),
        new("повтор сохрани", CommandIntent.SaveReplay),
        new("реплей сохрани", CommandIntent.SaveReplay),
        new("клип сохрани", CommandIntent.SaveReplay),
        new("повтор сохранить", CommandIntent.SaveReplay),
        new("реплей сохранить", CommandIntent.SaveReplay),
        new("клип сохранить", CommandIntent.SaveReplay),
        new("повтор запиши", CommandIntent.SaveReplay),
        new("сними повтор", CommandIntent.SaveReplay),
        new("снять повтор", CommandIntent.SaveReplay),
        new("сними клип", CommandIntent.SaveReplay),

        // Replay Buffer: включить.
        new("включи повтор", CommandIntent.StartReplayBuffer),
        new("включить повтор", CommandIntent.StartReplayBuffer),
        new("включи реплей", CommandIntent.StartReplayBuffer),
        new("включить реплей", CommandIntent.StartReplayBuffer),
        new("включи буфер повторов", CommandIntent.StartReplayBuffer),
        new("включить буфер повторов", CommandIntent.StartReplayBuffer),
        new("включи буфер повтора", CommandIntent.StartReplayBuffer),
        new("запусти буфер повторов", CommandIntent.StartReplayBuffer),
        new("включи replay buffer", CommandIntent.StartReplayBuffer),
        new("буфер повторов включи", CommandIntent.StartReplayBuffer),
        new("повтор включи", CommandIntent.StartReplayBuffer),
        new("реплей включи", CommandIntent.StartReplayBuffer),
        new("буфер включи", CommandIntent.StartReplayBuffer),
        new("буфер повторов запусти", CommandIntent.StartReplayBuffer),

        // Источники: микрофон и браузер.
        // «Включи» и «выключи» для микрофона — одно действие: переключение состояния.
        // Браузер сохраняет явный смысл: включить или выключить.
        // Формы «глагол + источник» собираются в BuildInputPhrases, чтобы не терять
        // комбинации вроде «замуть браузер». Здесь — только то, что не сводится к схеме.
        new("выключи звук микрофона", CommandIntent.ToggleInput, "microphone"),
        new("включи звук микрофона", CommandIntent.ToggleInput, "microphone"),
        new("снять мут с микрофона", CommandIntent.ToggleInput, "microphone"),
        new("сними мут с микрофона", CommandIntent.ToggleInput, "microphone"),
        new("снять мут", CommandIntent.ToggleInput, "microphone"),
        new("сними мут", CommandIntent.ToggleInput, "microphone"),
        new("убрать мут", CommandIntent.ToggleInput, "microphone"),

        // Короткая форма: одно слово «мут» переключает микрофон. Формы с
        // родительным падежом и фактическими вариантами Vosk добавлены точечно:
        // общее правило не исправляет слова из трёх букв, чтобы не переключать
        // микрофон на случайное короткое слово.
        new("мут", CommandIntent.ToggleInput, "microphone"),
        new("муть", CommandIntent.ToggleInput, "microphone"),
        new("мьют", CommandIntent.ToggleInput, "microphone"),
        new("муд", CommandIntent.ToggleInput, "microphone"),
        new("мод", CommandIntent.ToggleInput, "microphone"),
        new("мут микрофона", CommandIntent.ToggleInput, "microphone"),
        new("мог", CommandIntent.ToggleInput, "microphone"),
        new("мог микрофона", CommandIntent.ToggleInput, "microphone"),

        new("выключи звук браузера", CommandIntent.MuteInput, "browser"),
        new("выключить звук браузера", CommandIntent.MuteInput, "browser"),
        new("отключи звук браузера", CommandIntent.MuteInput, "browser"),
        new("выруби звук браузера", CommandIntent.MuteInput, "browser"),
        new("убери звук браузера", CommandIntent.MuteInput, "browser"),
        new("верни звук браузера", CommandIntent.UnmuteInput, "browser"),
        new("включи звук браузера", CommandIntent.UnmuteInput, "browser"),
        new("включить звук браузера", CommandIntent.UnmuteInput, "browser"),
        new("снять мут с браузера", CommandIntent.UnmuteInput, "browser"),
        new("сними мут с браузера", CommandIntent.UnmuteInput, "browser"),
        new("убрать мут с браузера", CommandIntent.UnmuteInput, "browser"),

        // Весь звук: выключаются все аудиоисточники OBS. Видеоисточники
        // без звука пропускаются адаптером.
        new("ол мут", CommandIntent.MuteAllAudio),
        new("олл мут", CommandIntent.MuteAllAudio),
        new("ол муть", CommandIntent.MuteAllAudio),
        new("олл муть", CommandIntent.MuteAllAudio),
        new("олмут", CommandIntent.MuteAllAudio),
        new("оллмут", CommandIntent.MuteAllAudio),
        new("олмуть", CommandIntent.MuteAllAudio),
        new("оллмуть", CommandIntent.MuteAllAudio),
        new("ол мьют", CommandIntent.MuteAllAudio),
        new("олл мьют", CommandIntent.MuteAllAudio),
        new("олмьют", CommandIntent.MuteAllAudio),
        new("оллмьют", CommandIntent.MuteAllAudio),
        new("all mute", CommandIntent.MuteAllAudio),
        new("allmute", CommandIntent.MuteAllAudio),
        new("выключи весь звук", CommandIntent.MuteAllAudio),
        new("выключить весь звук", CommandIntent.MuteAllAudio),
        new("выключи все звуки", CommandIntent.MuteAllAudio),
        new("заглуши все", CommandIntent.MuteAllAudio),
        new("заглушить все", CommandIntent.MuteAllAudio),
        new("мут все", CommandIntent.MuteAllAudio),
        new("все мут", CommandIntent.MuteAllAudio)
    };

    /// <summary>
    /// Допустимые формулировки. Список намеренно широкий: пользователь не должен
    /// подстраивать речь под приложение. Все фразы сопоставляются целиком.
    /// К явному списку добавляются систематические формы «глагол + источник».
    /// </summary>
    public static readonly IReadOnlyList<CommandPhrase> Phrases =
        ExplicitPhrases.Concat(BuildInputPhrases()).ToArray();

    /// <summary>Команды с аргументом после фиксированного префикса.</summary>
    public static readonly IReadOnlyList<ArgumentCommand> ArgumentCommands = new ArgumentCommand[]
    {
        new("измени название стрима на", CommandIntent.SetStreamTitle),
        new("измени название трансляции на", CommandIntent.SetStreamTitle),
        new("изменить название стрима на", CommandIntent.SetStreamTitle),
        new("поставь название стрима", CommandIntent.SetStreamTitle),
        new("смени название стрима на", CommandIntent.SetStreamTitle),
        new("измени категорию стрима на", CommandIntent.SetStreamCategory),
        new("измени категорию трансляции на", CommandIntent.SetStreamCategory),
        new("изменить категорию стрима на", CommandIntent.SetStreamCategory),
        new("поставь категорию стрима", CommandIntent.SetStreamCategory),
        new("смени категорию стрима на", CommandIntent.SetStreamCategory)
    };

    /// <summary>
    /// Канонические слова команд. Используются только для исправления отдельного слова
    /// при ошибке распознавания: фраза всё равно должна совпасть целиком.
    /// </summary>
    public static readonly IReadOnlySet<string> Tokens = BuildTokenList();

    /// <summary>
    /// Систематические формы «глагол + источник» и «источник + глагол».
    /// Собираются из <see cref="AudioTargets"/> и списков глаголов, поэтому новая
    /// формулировка глагола сразу работает со всеми источниками.
    /// </summary>
    private static IEnumerable<CommandPhrase> BuildInputPhrases()
    {
        foreach (var target in AudioTargets)
        {
            foreach (var name in BuildTargetNames(target))
            {
                foreach (var phrase in BuildVerbPhrases(target.Key, name, target.Reverse, OffVerbs, target.OffIntent))
                {
                    yield return phrase;
                }

                foreach (var phrase in BuildVerbPhrases(target.Key, name, target.Reverse, OnVerbs, target.OnIntent))
                {
                    yield return phrase;
                }
            }
        }
    }

    /// <summary>
    /// Слова, с которыми работает цель: либо само слово, либо все его уточнения.
    /// Так «музыка стрима» и «музыка на стриме» остаются одним источником и не
    /// дублируют глаголы вручную.
    /// </summary>
    private static IEnumerable<string> BuildTargetNames(AudioTarget target)
    {
        if (target.ScopeForms is null || target.ScopeForms.Count == 0)
        {
            yield return target.Name;
            yield break;
        }

        foreach (var scope in target.ScopeForms)
        {
            yield return $"{target.Name} {scope}";
        }
    }

    private static IEnumerable<CommandPhrase> BuildVerbPhrases(
        string target,
        string name,
        bool reverse,
        IReadOnlyList<string> verbs,
        CommandIntent intent)
    {
        foreach (var verb in verbs)
        {
            yield return new CommandPhrase($"{verb} {name}", intent, target);

            if (reverse)
            {
                yield return new CommandPhrase($"{name} {verb}", intent, target);
            }
        }
    }

    /// <summary>
    /// Противоположные глаголы: «включи» и «выключи». Фаззи-сопоставление
    /// не должно переворачивать команду из-за ошибки распознавания.
    /// </summary>
    public static bool AreOppositeVerbs(string left, string right) =>
        (OnVerbs.Contains(left) && OffVerbs.Contains(right)) ||
        (OffVerbs.Contains(left) && OnVerbs.Contains(right));

    /// <summary>Есть ли во фразе отрицание: такие фразы не разбираются.</summary>
    public static bool ContainsNegation(string normalizedText) =>
        normalizedText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(Negations.Contains);

    /// <summary>Убирает слова-заполнители, сохраняя порядок остальных слов.</summary>
    public static string RemoveFillers(string normalizedText) =>
        string.Join(
            ' ',
            normalizedText
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !Fillers.Contains(token)));

    /// <summary>
    /// Нормализация текста для сопоставления: нижний регистр, замена ё→е,
    /// удаление пунктуации и схлопывание пробелов.
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.Trim().ToLowerInvariant().Replace('ё', 'е');
        var builder = new StringBuilder(lowered.Length);
        foreach (var symbol in lowered)
        {
            // Дефис склеивает составное слово: Vosk слышит «как-то» одним словом.
            // Если разбить его на два токена, лишнее слово ломает сопоставление.
            if (symbol == '-')
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(symbol) ? symbol : ' ');
        }

        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlySet<string> BuildTokenList()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var phrase in Phrases)
        {
            AddTokens(tokens, phrase.Phrase);
        }

        foreach (var command in ArgumentCommands)
        {
            AddTokens(tokens, command.Prefix);
        }

        return tokens;
    }

    private static void AddTokens(HashSet<string> tokens, string phrase)
    {
        foreach (var token in Normalize(phrase).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Fillers.Contains(token) && !Negations.Contains(token))
            {
                tokens.Add(token);
            }
        }
    }
}
