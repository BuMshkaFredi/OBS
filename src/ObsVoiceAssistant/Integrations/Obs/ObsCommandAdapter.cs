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
