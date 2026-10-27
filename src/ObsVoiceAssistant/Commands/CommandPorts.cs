namespace ObsVoiceAssistant.Commands;

/// <summary>Итог переключения источника: сообщение и фактическое состояние.</summary>
public readonly record struct InputToggleResult(string? Message, bool? Muted);

public interface IObsCommandPort
{
    /// <summary>
    /// Выполняет команду. Возвращает сообщение для пользователя, если итог нельзя
    /// понять из самой команды (например, переключение микрофона).
    /// </summary>
    Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Переключает источник и возвращает фактическое состояние. Нужен командам,
    /// которые зависят от результата: пауза музыки выполняется только после mute.
    /// </summary>
    Task<InputToggleResult> ToggleInputAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default);
}

public enum MediaPlaybackOutcome
{
    Succeeded,
    PlayerNotFound,
    Failed
}

/// <summary>Результат управления плеером. Ошибка плеера не отменяет действие в OBS.</summary>
public sealed record MediaPlaybackResult(MediaPlaybackOutcome Outcome, string? Detail = null)
{
    public static MediaPlaybackResult Success() => new(MediaPlaybackOutcome.Succeeded);

    public static MediaPlaybackResult PlayerNotFound() => new(MediaPlaybackOutcome.PlayerNotFound);

    public static MediaPlaybackResult Failed(string detail) => new(MediaPlaybackOutcome.Failed, detail);
}

/// <summary>
/// Управление внешним плеером через медиа-сессии Windows. Реализация живёт
/// в <c>Integrations/Media</c>; диспетчер не знает про WinRT.
/// </summary>
public interface IMediaPlaybackControl
{
    Task<MediaPlaybackResult> PauseAsync(CancellationToken cancellationToken = default);

    Task<MediaPlaybackResult> NextTrackAsync(CancellationToken cancellationToken = default);
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
