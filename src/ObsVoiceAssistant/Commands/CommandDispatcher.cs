namespace ObsVoiceAssistant.Commands;

public sealed class CommandDispatcher
{
    private readonly IObsCommandPort obs;
    private readonly ITwitchCommandPort twitch;
    private readonly IConfirmationPolicy confirmationPolicy;

    public CommandDispatcher(
        IObsCommandPort obs,
        ITwitchCommandPort twitch,
        IConfirmationPolicy confirmationPolicy)
    {
        this.obs = obs;
        this.twitch = twitch;
        this.confirmationPolicy = confirmationPolicy;
    }

    public bool RequiresConfirmation(ParsedCommand command) =>
        confirmationPolicy.RequiresConfirmation(command.Intent);

    /// <summary>
    /// Выполняет проверку до подтверждения. Сейчас она нужна категории Twitch:
    /// пользователь должен увидеть в уведомлении реальное название с Twitch.
    /// </summary>
    public Task<ParsedCommand> PrepareAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default) =>
        command.Intent == CommandIntent.SetStreamCategory
            ? twitch.PrepareAsync(command, cancellationToken)
            : Task.FromResult(command);

    public async Task<string?> DispatchAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Intent is CommandIntent.SetStreamTitle or CommandIntent.SetStreamCategory)
        {
            return await twitch.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }

        return await obs.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
