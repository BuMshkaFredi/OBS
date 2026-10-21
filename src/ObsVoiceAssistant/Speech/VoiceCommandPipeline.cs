using ObsVoiceAssistant.Audio;
using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Notifications;

namespace ObsVoiceAssistant.Speech;

public sealed class VoiceCommandPipeline
{
    private readonly ISpeechRecognizer recognizer;
    private readonly CommandParser parser;
    private readonly CommandDispatcher dispatcher;
    private readonly IConfirmationService confirmationService;
    private readonly Action<string>? diagnostic;
    private readonly Action<string>? notify;
    private readonly Action<string>? compactNotify;

    public VoiceCommandPipeline(
        ISpeechRecognizer recognizer,
        CommandParser parser,
        CommandDispatcher dispatcher,
        IConfirmationService? confirmationService = null,
        Action<string>? diagnostic = null,
        Action<string>? notify = null,
        Action<string>? compactNotify = null)
    {
        this.recognizer = recognizer;
        this.parser = parser;
        this.dispatcher = dispatcher;
        this.confirmationService = confirmationService ?? new DecliningConfirmationService();
        this.diagnostic = diagnostic;
        this.notify = notify;
        // Короткие сообщения о потере команды показываются компактной панелью,
        // если она подключена; иначе используется обычное уведомление трея.
        this.compactNotify = compactNotify ?? notify;
    }

    private sealed class DecliningConfirmationService : IConfirmationService
    {
        public Task<bool> ConfirmAsync(ParsedCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    public async Task ProcessAsync(AudioRecording recording, CancellationToken cancellationToken = default)
    {
        if (recording.Data.IsEmpty)
        {
            diagnostic?.Invoke("Пустая запись: распознавание пропущено");
            return;
        }

        if (!SpeechActivityDetector.ContainsSpeech(recording))
        {
            diagnostic?.Invoke("Запись без речи: распознавание пропущено");
            return;
        }

        var text = await recognizer.RecognizeAsync(recording, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            // Речь была, но движок не дал текста: это потеря команды, а не тишина.
            diagnostic?.Invoke("Речь была, но текст не распознан");
            compactNotify?.Invoke("Не расслышал команду");
            return;
        }

        await ProcessTextAsync(text, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Выполняет команду по уже распознанному тексту. Нужен режиму ключевого слова:
    /// сначала текст проверяется на «Бочи», и только остаток уходит в обычный разбор.
    /// </summary>
    public async Task ProcessTextAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            diagnostic?.Invoke("Пустой текст: разбор команды пропущен");
            compactNotify?.Invoke("Не расслышал команду");
            return;
        }

        var parseResult = parser.ParseWithDetails(text);
        var command = parseResult.Command;
        if (command is null)
        {
            // Текст без команды — главный источник «сказал, а ничего не произошло».
            // В лог пишем саму фразу и ближайшую словарную, но не готовую строку
            // new(...): решение о расширении словаря принимает человек.
            diagnostic?.Invoke(DescribeMiss(parseResult.NormalizedText, text));
            compactNotify?.Invoke("Команда не распознана");
            return;
        }

        if (parseResult.Corrections.Count > 0)
        {
            diagnostic?.Invoke($"Исправлено распознавание: {Describe(parseResult.Corrections)}");
        }

        diagnostic?.Invoke($"Распознана команда: {command.Intent}");

        if (dispatcher.RequiresConfirmation(command))
        {
            diagnostic?.Invoke($"Требуется подтверждение: {command.Intent}");
            if (!await confirmationService.ConfirmAsync(command, cancellationToken).ConfigureAwait(false))
            {
                diagnostic?.Invoke($"Команда отклонена: {command.Intent}");
                return;
            }
        }

        string? outcome;
        try
        {
            outcome = await dispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Команда распознана, но не выполнена. Молчать нельзя: пользователь
            // повторит фразу, не понимая, что именно сломалось.
            diagnostic?.Invoke(
                $"Ошибка выполнения {command.Intent}: {exception.GetType().Name}: {exception.Message}");
            notify?.Invoke($"Не удалось выполнить «{Describe(command)}»: {Truncate(exception.Message)}");
            return;
        }

        diagnostic?.Invoke($"Команда отправлена: {command.Intent}");

        // Запуск записи не спрашивает подтверждения, поэтому пользователь должен
        // увидеть, что команда действительно сработала.
        outcome ??= DescribeSuccess(command);

        // Переключение микрофона нельзя проверить по самой команде:
        // «включи» могло выключить. Поэтому сообщаем фактический результат.
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            diagnostic?.Invoke($"Результат: {outcome}");
            notify?.Invoke(outcome);
        }
    }

    private string DescribeMiss(string normalizedText, string rawText)
    {
        var message = $"Команда не распознана: «{rawText}»";
        var suggestion = parser.SuggestClosest(normalizedText);
        if (suggestion is null)
        {
            return message;
        }

        var closest = CommandVocabulary.Normalize(suggestion.Phrase.Phrase);

        // Это только диагностическая подсказка. Готовую строку new(...) не печатаем:
        // короткие и неоднозначные слова должен решать человек, а не лог.
        return $"{message} (ближайшая фраза: «{closest}» → {suggestion.Phrase.Intent})";
    }

    private static string Describe(IReadOnlyList<TokenCorrection> corrections) =>
        string.Join(", ", corrections.Select(correction => $"«{correction.From}»→«{correction.To}»"));

    private static string? DescribeSuccess(ParsedCommand command) => command.Intent switch
    {
        CommandIntent.StartRecording => "Запись началась",
        CommandIntent.MuteAllAudio => "Весь звук выключен",
        _ => null
    };

    private static string Describe(ParsedCommand command) => command.Intent switch
    {
        CommandIntent.StartRecording => "начать запись",
        CommandIntent.StopRecording => "остановить запись",
        CommandIntent.StartStream => "начать стрим",
        CommandIntent.StopStream => "остановить стрим",
        CommandIntent.SaveReplay => "сохранить повтор",
        CommandIntent.StartReplayBuffer => "включить повтор",
        CommandIntent.MuteInput => $"выключить {CommandTargets.Accusative(command.Target)}",
        CommandIntent.UnmuteInput => $"включить {CommandTargets.Accusative(command.Target)}",
        CommandIntent.ToggleInput => $"переключить {CommandTargets.Accusative(command.Target)}",
        CommandIntent.AdjustInputVolume => $"изменить громкость {CommandTargets.Genitive(command.Target)}",
        CommandIntent.MuteAllAudio => "выключить весь звук",
        CommandIntent.SetStreamTitle => "изменить название",
        CommandIntent.SetStreamCategory => "изменить категорию",
        _ => command.Intent.ToString()
    };

    private static string Truncate(string value) =>
        value.Length <= 160 ? value : value[..157] + "...";
}
