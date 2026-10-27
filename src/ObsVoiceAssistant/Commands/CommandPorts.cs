namespace ObsVoiceAssistant.Commands;

public interface IObsCommandPort
{
    /// <summary>
    /// Выполняет команду. Возвращает сообщение для пользователя, если итог нельзя
    /// понять из самой команды (например, переключение микрофона).
    /// </summary>
    Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default);
}

public interface ITwitchCommandPort
{
    /// <summary>
    /// Проверяет команду до подтверждения: для категории находит её на Twitch
    /// и подставляет точное название и game_id. Остальные команды возвращает без изменений.
    /// </summary>
    Task<ParsedCommand> PrepareAsync(ParsedCommand command, CancellationToken cancellationToken = default);

    Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default);
}

public interface IConfirmationPolicy
{
    bool RequiresConfirmation(CommandIntent intent);
}

public sealed class ConfirmationPolicy : IConfirmationPolicy
{
    public bool RequiresConfirmation(CommandIntent intent) => intent switch
    {
        CommandIntent.StartStream => true,
        CommandIntent.StopRecording => true,
        CommandIntent.StopStream => true,
        CommandIntent.SetStreamTitle => true,
        CommandIntent.SetStreamCategory => true,
        _ => false
    };
}
