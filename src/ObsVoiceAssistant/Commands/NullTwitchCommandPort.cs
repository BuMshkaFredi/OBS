namespace ObsVoiceAssistant.Commands;

public sealed class NullTwitchCommandPort : ITwitchCommandPort
{
    public Task<ParsedCommand> PrepareAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(command);

    public Task<string?> ExecuteAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Twitch integration is not configured yet.");
}
