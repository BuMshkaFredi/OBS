namespace ObsVoiceAssistant.Commands;

public sealed class NullTwitchCommandPort : ITwitchCommandPort
{
    public Task ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Twitch integration is not configured yet.");
}
