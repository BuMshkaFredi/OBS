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

    public async Task<string?> DispatchAsync(ParsedCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Intent is CommandIntent.SetStreamTitle or CommandIntent.SetStreamCategory)
        {
            await twitch.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
            return null;
        }

        return await obs.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
