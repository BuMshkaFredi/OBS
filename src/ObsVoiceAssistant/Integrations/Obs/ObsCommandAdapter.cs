using ObsVoiceAssistant.Commands;

namespace ObsVoiceAssistant.Integrations.Obs;

public interface IObsClient
{
    bool IsConnected { get; }
    bool IsReplayBufferActive { get; }
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task StartRecordingAsync(CancellationToken cancellationToken = default);
    Task StopRecordingAsync(CancellationToken cancellationToken = default);
    Task StartStreamAsync(CancellationToken cancellationToken = default);
    Task StopStreamAsync(CancellationToken cancellationToken = default);
    Task StartReplayBufferAsync(CancellationToken cancellationToken = default);
    Task SaveReplayAsync(CancellationToken cancellationToken = default);
    Task SetInputMuteAsync(string inputName, bool muted, CancellationToken cancellationToken = default);

    /// <summary>
    /// Переключает mute источника и возвращает новое состояние.
    /// <c>null</c> — переключение выполнено, но прочитать состояние не удалось.
    /// </summary>
    Task<bool?> ToggleInputMuteAsync(string inputName, CancellationToken cancellationToken = default);

    /// <summary>Текущая громкость источника в долях от 0.0 до 1.0.</summary>
    Task<float> GetInputVolumeAsync(string inputName, CancellationToken cancellationToken = default);

    /// <summary>Устанавливает громкость источника в долях от 0.0 до 1.0.</summary>
    Task SetInputVolumeAsync(
        string inputName,
        float volumeMultiplier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Выключает звук у всех источников OBS, которые поддерживают аудио.
    /// Видеоисточники без звука пропускаются.
    /// </summary>
    Task<ObsMuteAllResult> MuteAllAudioAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetInputNamesAsync(CancellationToken cancellationToken = default);
}

public interface IObsInputResolver
{
    string? Resolve(string target);
}

public sealed class ObsCommandAdapter : IObsCommandPort
{
    private readonly IObsClient client;
    private readonly IObsInputResolver inputResolver;

    public ObsCommandAdapter(IObsClient client, IObsInputResolver inputResolver)
    {
        this.client = client;
        this.inputResolver = inputResolver;
    }

    public Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default) =>
        command.Intent switch
        {
            CommandIntent.StartRecording => Run(client.StartRecordingAsync(cancellationToken)),
            CommandIntent.StopRecording => Run(client.StopRecordingAsync(cancellationToken)),
            CommandIntent.StartStream => Run(client.StartStreamAsync(cancellationToken)),
            CommandIntent.StopStream => Run(client.StopStreamAsync(cancellationToken)),
            CommandIntent.StartReplayBuffer => Run(client.StartReplayBufferAsync(cancellationToken)),
            CommandIntent.SaveReplay => Run(client.SaveReplayAsync(cancellationToken)),
            CommandIntent.MuteInput => Run(SetMuteAsync(command, true, cancellationToken)),
            CommandIntent.UnmuteInput => Run(SetMuteAsync(command, false, cancellationToken)),
            CommandIntent.ToggleInput => ToggleMuteAsync(command, cancellationToken),
            CommandIntent.AdjustInputVolume => AdjustVolumeAsync(command, cancellationToken),
            CommandIntent.MuteAllAudio => MuteAllAudioAsync(cancellationToken),
            _ => throw new InvalidOperationException($"Intent {command.Intent} is not an OBS command.")
        };

    private static async Task<string?> Run(Task operation)
    {
        await operation.ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// Переключает состояние источника. Сообщение о результате нужно потому,
    /// что при переключении формулировка команды не предсказывает итог.
    /// </summary>
    private async Task<string?> ToggleMuteAsync(ParsedCommand command, CancellationToken cancellationToken)
    {
        var inputName = ResolveInputName(command);
        var muted = await client.ToggleInputMuteAsync(inputName, cancellationToken).ConfigureAwait(false);

        return DescribeToggleResult(command.Target, inputName, muted);
    }

    /// <summary>
    /// Сообщение о фактическом состоянии после переключения. Род и название
    /// зависят от источника, поэтому строки заданы целиком.
    /// </summary>
    private static string DescribeToggleResult(string? target, string inputName, bool? muted)
    {
        (string On, string Off, string Toggled) messages = target switch
        {
            "microphone" => ("Микрофон включён", "Микрофон выключен", "Микрофон переключён"),
            "personal_music" => ("Личная музыка включена", "Личная музыка выключена", "Личная музыка переключена"),
            _ => ($"Источник «{inputName}» включён", $"Источник «{inputName}» выключен", $"Источник «{inputName}» переключён")
        };

        return muted switch
        {
            true => messages.Off,
            false => messages.On,
            null => messages.Toggled
        };
    }

    /// <summary>
    /// Меняет громкость источника на заданное число процентных пунктов.
    /// Итог ограничивается диапазоном 0–100%: на максимуме «плюс 10» оставляет
    /// 100%, а не уводит OBS в неожиданное значение.
    /// </summary>
    private async Task<string?> AdjustVolumeAsync(
        ParsedCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Delta is null)
        {
            throw new InvalidOperationException("Volume delta is missing.");
        }

        var inputName = ResolveInputName(command);
        var current = await client.GetInputVolumeAsync(inputName, cancellationToken).ConfigureAwait(false);
        var volume = Math.Clamp(current + command.Delta.Value / 100f, 0f, 1f);
        await client.SetInputVolumeAsync(inputName, volume, cancellationToken).ConfigureAwait(false);

        var percent = (int)Math.Round(volume * 100, MidpointRounding.AwayFromZero);
        return $"Громкость {CommandTargets.Genitive(command.Target)} {percent}%";
    }

    /// <summary>
    /// Выключает звук у всех аудиоисточников OBS и сообщает итог. Счётчик
    /// подтверждает, что команда действительно что-то сделала, а список
    /// ошибок — что именно осталось включённым.
    /// </summary>
    private async Task<string?> MuteAllAudioAsync(CancellationToken cancellationToken)
    {
        if (!client.IsConnected)
        {
            throw new InvalidOperationException("OBS не подключена");
        }

        var result = await client.MuteAllAudioAsync(cancellationToken).ConfigureAwait(false);

        if (result.FailedInputs.Count > 0)
        {
            return $"Звук выключен не полностью. Не удалось: {string.Join(", ", result.FailedInputs)}";
        }

        return result.MutedCount == 0
            ? "В OBS нет источников со звуком"
            : $"Весь звук выключен (источников: {result.MutedCount})";
    }

    private Task SetMuteAsync(ParsedCommand command, bool muted, CancellationToken cancellationToken) =>
        client.SetInputMuteAsync(ResolveInputName(command), muted, cancellationToken);

    private string ResolveInputName(ParsedCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            throw new InvalidOperationException("OBS input target is missing.");
        }

        var inputName = inputResolver.Resolve(command.Target);
        if (string.IsNullOrWhiteSpace(inputName))
        {
            // Ожидаемая ошибка настройки, поэтому сообщение должно объяснять, что делать.
            throw new InvalidOperationException(
                $"Источник OBS для {CommandTargets.Genitive(command.Target)} не настроен. " +
                "Укажите имя источника в настройках приложения: значок в трее -> Настройка OBS -> Источники.");
        }

        return inputName;
    }
}
