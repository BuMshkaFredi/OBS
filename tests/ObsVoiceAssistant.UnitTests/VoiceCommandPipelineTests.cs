using ObsVoiceAssistant.Audio;
using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Notifications;
using ObsVoiceAssistant.Speech;

namespace ObsVoiceAssistant.UnitTests;

public sealed class VoiceCommandPipelineTests
{
    [Fact]
    public async Task RecognizedSafeCommand_IsDispatched()
    {
        var port = new RecordingPort();
        var pipeline = CreatePipeline("\u043d\u0430\u0447\u043d\u0438 \u0437\u0430\u043f\u0438\u0441\u044c", port, true);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Equal(CommandIntent.StartRecording, port.LastCommand?.Intent);
    }

    [Fact]
    public async Task DangerousCommand_IsNotDispatchedWhenConfirmationIsDeclined()
    {
        var port = new RecordingPort();
        var confirmation = new FakeConfirmationService(false);
        var pipeline = CreatePipeline("\u043e\u0441\u0442\u0430\u043d\u043e\u0432\u0438 \u0437\u0430\u043f\u0438\u0441\u044c", port, confirmation);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Null(port.LastCommand);
        Assert.Equal(CommandIntent.StopRecording, confirmation.LastCommand?.Intent);
    }

    [Fact]
    public async Task DangerousCommand_IsDispatchedOnlyAfterConfirmation()
    {
        var port = new RecordingPort();
        var pipeline = CreatePipeline("\u043e\u0441\u0442\u0430\u043d\u043e\u0432\u0438 \u0437\u0430\u043f\u0438\u0441\u044c", port, true);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Equal(CommandIntent.StopRecording, port.LastCommand?.Intent);
    }

    [Fact]
    public async Task SilentSegment_IsNotRecognized()
    {
        var port = new RecordingPort();
        var pipeline = CreatePipeline("\u043d\u0430\u0447\u043d\u0438 \u0437\u0430\u043f\u0438\u0441\u044c", port, true);

        await pipeline.ProcessAsync(CreateSilentRecording());

        Assert.Null(port.LastCommand);
    }

    [Fact]
    public async Task DispatchedCommand_IsReportedThroughDiagnostic()
    {
        var port = new RecordingPort();
        var messages = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("\u043d\u0430\u0447\u043d\u0438 \u0437\u0430\u043f\u0438\u0441\u044c"),
            new CommandParser(),
            new CommandDispatcher(port, new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            diagnostic: messages.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(messages, message => message.Contains("StartRecording", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.StartsWith("\u041a\u043e\u043c\u0430\u043d\u0434\u0430 \u043e\u0442\u043f\u0440\u0430\u0432\u043b\u0435\u043d\u0430", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeclinedDangerousCommand_IsReportedThroughDiagnostic()
    {
        var port = new RecordingPort();
        var messages = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("\u043e\u0441\u0442\u0430\u043d\u043e\u0432\u0438 \u0437\u0430\u043f\u0438\u0441\u044c"),
            new CommandParser(),
            new CommandDispatcher(port, new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(false),
            diagnostic: messages.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(messages, message => message.StartsWith("\u0422\u0440\u0435\u0431\u0443\u0435\u0442\u0441\u044f \u043f\u043e\u0434\u0442\u0432\u0435\u0440\u0436\u0434\u0435\u043d\u0438\u0435", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.StartsWith("\u041a\u043e\u043c\u0430\u043d\u0434\u0430 \u043e\u0442\u043a\u043b\u043e\u043d\u0435\u043d\u0430", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message.StartsWith("\u041a\u043e\u043c\u0430\u043d\u0434\u0430 \u043e\u0442\u043f\u0440\u0430\u0432\u043b\u0435\u043d\u0430", StringComparison.Ordinal));
        Assert.Null(port.LastCommand);
    }

    [Fact]
    public async Task UnrecognizedText_IsReportedThroughDiagnostic()
    {
        // Молчаливый пропуск — главная причина «сказал, а ничего не произошло».
        var messages = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("привет как дела"),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            diagnostic: messages.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(messages, message => message.Contains("Команда не распознана", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("привет как дела", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnrecognizedText_NotifiesUser()
    {
        // Заказчик попросил не молчать, а показывать маленькое уведомление.
        var notified = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("привет как дела"),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            notify: notified.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(notified, message => message == "Команда не распознана");
    }

    [Fact]
    public async Task EmptyRecognition_NotifiesUser()
    {
        var notified = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer(string.Empty),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            notify: notified.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(notified, message => message == "Не расслышал команду");
    }

    [Fact]
    public async Task UnrecognizedText_PrefersCompactNotification()
    {
        // Короткие сообщения о потере команды не должны занимать крупный баллон трея.
        var compact = new List<string>();
        var regular = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("привет как дела"),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            notify: regular.Add,
            compactNotify: compact.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(compact, message => message == "Команда не распознана");
        Assert.Empty(regular);
    }

    [Fact]
    public async Task EmptyRecognition_PrefersCompactNotification()
    {
        var compact = new List<string>();
        var regular = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer(string.Empty),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            notify: regular.Add,
            compactNotify: compact.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(compact, message => message == "Не расслышал команду");
        Assert.Empty(regular);
    }

    [Fact]
    public async Task StartRecording_NotifiesUserAfterSuccess()
    {
        // Запись запускается без подтверждения, поэтому итог должен быть виден.
        var notified = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("начни запись"),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            notify: notified.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(notified, message => message == "Запись началась");
    }

    [Fact]
    public async Task StartStream_RequiresConfirmationBeforeDispatch()
    {
        var port = new RecordingPort();
        var confirmation = new FakeConfirmationService(true);
        var pipeline = CreatePipeline("начни стрим", port, confirmation);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Equal(CommandIntent.StartStream, confirmation.LastCommand?.Intent);
        Assert.Equal(CommandIntent.StartStream, port.LastCommand?.Intent);
    }

    [Fact]
    public async Task StartStream_IsNotDispatchedWhenConfirmationIsDeclined()
    {
        var port = new RecordingPort();
        var confirmation = new FakeConfirmationService(false);
        var pipeline = CreatePipeline("начни стрим", port, confirmation);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Equal(CommandIntent.StartStream, confirmation.LastCommand?.Intent);
        Assert.Null(port.LastCommand);
    }

    [Fact]
    public async Task UnrecognizedText_LogsClosestPhraseWithoutVocabularySuggestion()
    {
        // Лог подсказывает ближайшую фразу, но не печатает готовую строку new(...):
        // расширение словаря остаётся решением человека.
        var messages = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("включи браузер же"),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            diagnostic: messages.Add);

        await pipeline.ProcessAsync(CreateRecording());

        var miss = messages.Find(message => message.Contains("Команда не распознана", StringComparison.Ordinal));
        Assert.NotNull(miss);
        Assert.Contains("ближайшая фраза", miss, StringComparison.Ordinal);
        Assert.DoesNotContain("Добавить в CommandVocabulary", miss, StringComparison.Ordinal);
        Assert.DoesNotContain("new(", miss, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnrecognizedNegation_DoesNotSuggestMicrophoneToggle()
    {
        // «Не» — не команда и не повод предлагать new("не", ToggleInput, "microphone").
        var messages = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("не"),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            diagnostic: messages.Add);

        await pipeline.ProcessAsync(CreateRecording());

        var miss = messages.Find(message => message.Contains("Команда не распознана", StringComparison.Ordinal));
        Assert.NotNull(miss);
        Assert.DoesNotContain("ближайшая фраза", miss, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleInput", miss, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyRecognition_IsReportedThroughDiagnostic()
    {
        // Речь была, но движок не дал текста: это потеря команды, а не тишина.
        var messages = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer(string.Empty),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            diagnostic: messages.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(messages, message => message.Contains("текст не распознан", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedCommand_NotifiesUserInsteadOfFailingSilently()
    {
        var messages = new List<string>();
        var notified = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("начни запись"),
            new CommandParser(),
            new CommandDispatcher(new FailingPort("OBS недоступна"), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            diagnostic: messages.Add,
            notify: notified.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(messages, message => message.StartsWith("Ошибка выполнения", StringComparison.Ordinal));
        Assert.Contains(notified, message => message.Contains("начать запись", StringComparison.Ordinal));
        Assert.Contains(notified, message => message.Contains("OBS недоступна", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToggleCommand_ReportsResultToUser()
    {
        // При переключении слово в команде не предсказывает итог,
        // поэтому пользователь должен увидеть фактическое состояние.
        var notified = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("включи микрофон"),
            new CommandParser(),
            new CommandDispatcher(new OutcomePort("Микрофон выключен"), new NullTwitchCommandPort(), new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            notify: notified.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(notified, message => message == "Микрофон выключен");
    }

    [Fact]
    public async Task MuteAllAudio_ReportsResultToUser()
    {
        // «Ол мут» — массовое действие, поэтому итог должен быть виден.
        var notified = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("ол мут"),
            new CommandParser(),
            new CommandDispatcher(
                new OutcomePort("Весь звук выключен (источников: 5)"),
                new NullTwitchCommandPort(),
                new ConfirmationPolicy()),
            new FakeConfirmationService(true),
            notify: notified.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Contains(notified, message => message == "Весь звук выключен (источников: 5)");
    }

    [Fact]
    public async Task Category_IsPreparedBeforeConfirmationAndDispatch()
    {
        var twitch = new PreparingTwitchPort();
        var confirmation = new FakeConfirmationService(true);
        var notified = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("измени категорию стрима на dota 2"),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), twitch, new ConfirmationPolicy()),
            confirmation,
            notify: notified.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.NotNull(confirmation.LastCommand);
        Assert.Equal("Dota 2", confirmation.LastCommand.Argument);
        Assert.Equal("game-1", confirmation.LastCommand.ArgumentId);
        Assert.Equal("game-1", twitch.LastCommand?.ArgumentId);
        Assert.Contains(notified, message => message.Contains("Dota 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Category_PrepareFailure_NotifiesAndDoesNotAskForConfirmation()
    {
        var twitch = new FailingPrepareTwitchPort();
        var confirmation = new FakeConfirmationService(true);
        var notified = new List<string>();
        var pipeline = new VoiceCommandPipeline(
            new FakeRecognizer("измени категорию стрима на dota 2"),
            new CommandParser(),
            new CommandDispatcher(new RecordingPort(), twitch, new ConfirmationPolicy()),
            confirmation,
            notify: notified.Add);

        await pipeline.ProcessAsync(CreateRecording());

        Assert.Null(confirmation.LastCommand);
        Assert.Null(twitch.LastCommand);
        Assert.Contains(
            notified,
            message => message.Contains("Не удалось найти категорию", StringComparison.Ordinal));
    }

    private sealed class PreparingTwitchPort : ITwitchCommandPort
    {
        public ParsedCommand? LastCommand { get; private set; }

        public Task<ParsedCommand> PrepareAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(command with
            {
                Argument = "Dota 2",
                ArgumentId = "game-1"
            });

        public Task<string?> ExecuteAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult<string?>($"Категория изменена: «{command.Argument}»");
        }
    }

    private sealed class FailingPrepareTwitchPort : ITwitchCommandPort
    {
        public ParsedCommand? LastCommand { get; private set; }

        public Task<ParsedCommand> PrepareAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Twitch не подключён.");

        public Task<string?> ExecuteAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FailingPort(string message) : IObsCommandPort
    {
        public Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);

        public Task<InputToggleResult> ToggleInputAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class OutcomePort(string outcome) : IObsCommandPort
    {
        public Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(outcome);

        public Task<InputToggleResult> ToggleInputAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new InputToggleResult(outcome, null));
    }

    private static VoiceCommandPipeline CreatePipeline(string text, RecordingPort port, bool confirmed) =>
        CreatePipeline(text, port, new FakeConfirmationService(confirmed));

    private static VoiceCommandPipeline CreatePipeline(
        string text,
        RecordingPort port,
        IConfirmationService confirmation) =>
        new(
            new FakeRecognizer(text),
            new CommandParser(),
            new CommandDispatcher(port, new NullTwitchCommandPort(), new ConfirmationPolicy()),
            confirmation);

    private static AudioRecording CreateRecording() => new(CreateLoudPcm(1600), 16_000, 1);

    private static AudioRecording CreateSilentRecording() => new(new byte[1600], 16_000, 1);

    private static byte[] CreateLoudPcm(int byteCount)
    {
        // Синтетический сигнал с амплитудой выше порога детектора речи.
        var samples = new byte[byteCount];
        const short amplitude = 4000;
        for (var index = 0; index + 1 < samples.Length; index += 2)
        {
            samples[index] = (byte)(amplitude & 0xFF);
            samples[index + 1] = (byte)((amplitude >> 8) & 0xFF);
        }

        return samples;
    }

    private sealed class FakeRecognizer(string text) : ISpeechRecognizer
    {
        public Task<string?> RecognizeAsync(AudioRecording recording, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(text);
    }

    private sealed class FakeConfirmationService(bool result) : IConfirmationService
    {
        public ParsedCommand? LastCommand { get; private set; }

        public Task<bool> ConfirmAsync(ParsedCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingPort : IObsCommandPort
    {
        public ParsedCommand? LastCommand { get; private set; }

        public Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult<string?>(null);
        }

        public Task<InputToggleResult> ToggleInputAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(new InputToggleResult(null, null));
        }
    }
}
