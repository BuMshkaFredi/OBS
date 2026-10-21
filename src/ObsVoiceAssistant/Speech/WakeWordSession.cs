using ObsVoiceAssistant.Commands;

namespace ObsVoiceAssistant.Speech;

/// <summary>Что делать с распознанной фразой в режиме ключевого слова.</summary>
public enum WakeWordDecisionKind
{
    /// <summary>Ключевого слова нет и окно ожидания команды не открыто — фразу игнорируем.</summary>
    Ignored,

    /// <summary>Прозвучало только ключевое слово: следующая фраза будет командой.</summary>
    Armed,

    /// <summary>Фраза содержит команду после ключевого слова или пришла в открытом окне.</summary>
    Command
}

/// <summary>Готовая к выполнению фраза без ключевого слова.</summary>
public readonly record struct WakeWordDecision(WakeWordDecisionKind Kind, string CommandText = "")
{
    public static WakeWordDecision Ignored => new(WakeWordDecisionKind.Ignored);

    public static WakeWordDecision Armed => new(WakeWordDecisionKind.Armed);

    public static WakeWordDecision Command(string commandText) =>
        new(WakeWordDecisionKind.Command, commandText);
}

/// <summary>
/// Разбор фраз для активации по ключевому слову «Бочи». Команду можно сказать сразу
/// после слова («Бочи, включи микрофон») или отдельной фразой в течение короткого окна
/// («Бочи» — пауза — «включи микрофон»).
/// </summary>
public sealed class WakeWordSession
{
    /// <summary>Ключевое слово, которое произносит пользователь.</summary>
    public const string WakeWord = "бочи";

    /// <summary>
    /// Варианты, которые реально выдаёт Vosk на слово «Бочи». Список закрытый и
    /// составлен по фактическим логам, а не фаззи-сопоставлением: «ночи» или «почки»
    /// отличаются на одну букву, но обращением не являются. Формы «бочка/бочки/бочке/бочек»
    /// и «боже» добавлены потому, что Vosk слышит имя так. Это обычные слова, поэтому
    /// ключевое слово принимается только в начале фразы.
    /// </summary>
    private static readonly IReadOnlySet<string> SpokenForms = new HashSet<string>(StringComparer.Ordinal)
    {
        "бочи",
        "боче",
        "бочий",
        "бочии",
        "бочь",
        "боч",
        "бочка",
        "бочки",
        "бочке",
        "бочек",
        "боже"
    };

    /// <summary>
    /// Ключевое слово с допуском в один символ. Первая буква обязательно остаётся «б»:
    /// иначе «ночи» (одна правка от «бочи») включало бы помощника на обычной речи.
    /// Допуск применяется и к длинным вариантам, поэтому «бочку» распознаётся как «бочка».
    /// </summary>
    private static bool IsWakeWord(string token)
    {
        if (SpokenForms.Contains(token))
        {
            return true;
        }

        if (token.Length < 3 || token[0] != 'б')
        {
            return false;
        }

        foreach (var form in SpokenForms)
        {
            if (IsOneEditAway(token, form))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Расстояние Левенштейна не больше единицы.</summary>
    private static bool IsOneEditAway(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1)
        {
            return false;
        }

        if (left.Length == right.Length)
        {
            var differences = 0;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] == right[index])
                {
                    continue;
                }

                differences++;
                if (differences > 1)
                {
                    return false;
                }
            }

            return differences == 1;
        }

        // Длины отличаются на единицу: ищем один пропущенный символ.
        var shorter = left.Length < right.Length ? left : right;
        var longer = left.Length < right.Length ? right : left;
        var shortIndex = 0;
        var longIndex = 0;
        var skipped = false;

        while (shortIndex < shorter.Length && longIndex < longer.Length)
        {
            if (shorter[shortIndex] == longer[longIndex])
            {
                shortIndex++;
                longIndex++;
                continue;
            }

            if (skipped)
            {
                return false;
            }

            skipped = true;
            longIndex++;
        }

        return true;
    }

    private readonly TimeSpan commandWindow;
    private readonly Func<DateTimeOffset> clock;

    // 0 — окно закрыто. Отдельное поле с Interlocked: Reset приходит из хука Caps Lock,
    // а Handle — из потока распознавания.
    private long armedUntilTicks;

    public WakeWordSession(TimeSpan? commandWindow = null, Func<DateTimeOffset>? clock = null)
    {
        this.commandWindow = commandWindow ?? TimeSpan.FromSeconds(6);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Сколько времени после одинокого «Бочи» следующая фраза считается командой.</summary>
    public TimeSpan CommandWindow => commandWindow;

    /// <summary>Открыто ли окно ожидания команды после ключевого слова.</summary>
    public bool IsArmed
    {
        get
        {
            var until = Interlocked.Read(ref armedUntilTicks);
            return until != 0 && clock().UtcDateTime.Ticks < until;
        }
    }

    /// <summary>
    /// Закрывает окно ожидания. Нужно, когда команда уже выполнена другим способом,
    /// например через Push-to-talk.
    /// </summary>
    public void Reset() => Disarm();

    /// <summary>
    /// Разбирает распознанный текст. Возвращает команду без ключевого слова,
    /// признак открытого окна или решение игнорировать фоновую речь.
    /// </summary>
    public WakeWordDecision Handle(string? recognizedText)
    {
        if (string.IsNullOrWhiteSpace(recognizedText))
        {
            return WakeWordDecision.Ignored;
        }

        var normalized = CommandVocabulary.Normalize(recognizedText);
        if (normalized.Length == 0)
        {
            return WakeWordDecision.Ignored;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Ключевое слово должно стоять в начале фразы (допустимы filler-слова вроде «ну»).
        // Это важно для вариантов «бочка/бочки/бочке»: это обычные слова, и искать их
        // в середине фразы значило бы открывать микрофон на фоновой речи.
        var start = 0;
        while (start < tokens.Length && CommandVocabulary.Fillers.Contains(tokens[start]))
        {
            start++;
        }

        if (start < tokens.Length && IsWakeWord(tokens[start]))
        {
            var remainder = string.Join(' ', tokens[(start + 1)..]);

            // «Бочи, пожалуйста» — это всё ещё только обращение: команду ждём следующей фразой.
            if (CommandVocabulary.RemoveFillers(remainder).Length == 0)
            {
                Arm();
                return WakeWordDecision.Armed;
            }

            Disarm();
            return WakeWordDecision.Command(remainder);
        }

        if (IsArmed)
        {
            Disarm();
            return WakeWordDecision.Command(normalized);
        }

        return WakeWordDecision.Ignored;
    }

    private void Arm() =>
        Interlocked.Exchange(ref armedUntilTicks, clock().Add(commandWindow).UtcDateTime.Ticks);

    private void Disarm() => Interlocked.Exchange(ref armedUntilTicks, 0);
}
