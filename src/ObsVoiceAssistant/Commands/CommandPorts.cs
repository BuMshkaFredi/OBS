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
    Task ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default);
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
