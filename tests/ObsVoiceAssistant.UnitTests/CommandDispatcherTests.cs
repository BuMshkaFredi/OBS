using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.UnitTests;

public sealed class CommandDispatcherTests
{
    [Fact]
    public async Task ObsCommand_IsForwardedToObsPort()
    {
        var obs = new RecordingObsPort();
        var dispatcher = new CommandDispatcher(obs, new NullTwitchCommandPort(), new ConfirmationPolicy());
        var command = new ParsedCommand(CommandIntent.StartRecording);

        await dispatcher.DispatchAsync(command);

        Assert.Same(command, obs.LastCommand);
    }

    private sealed class RecordingObsPort : IObsCommandPort
    {
        public ParsedCommand? LastCommand { get; private set; }

        public Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult<string?>(null);
        }
    }
}
