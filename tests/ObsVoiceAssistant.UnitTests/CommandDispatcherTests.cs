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

    [Fact]
    public async Task TwitchCommand_IsForwardedToTwitchPortAndOutcomeIsReturned()
    {
        var twitch = new RecordingTwitchPort();
        var dispatcher = new CommandDispatcher(
            new RecordingObsPort(),
            twitch,
            new ConfirmationPolicy());
        var command = new ParsedCommand(CommandIntent.SetStreamTitle, "Название");

        var outcome = await dispatcher.DispatchAsync(command);

        Assert.Same(command, twitch.LastCommand);
        Assert.Equal("готово", outcome);
    }

    [Fact]
    public async Task PrepareCategory_IsForwardedToTwitchPort()
    {
        var twitch = new RecordingTwitchPort();
        var dispatcher = new CommandDispatcher(
            new RecordingObsPort(),
            twitch,
            new ConfirmationPolicy());
        var command = new ParsedCommand(CommandIntent.SetStreamCategory, "категория");

        var prepared = await dispatcher.PrepareAsync(command);

        Assert.Same(command, twitch.LastPrepared);
        Assert.Equal("Точная категория", prepared.Argument);
        Assert.Equal("game-1", prepared.ArgumentId);
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

    private sealed class RecordingTwitchPort : ITwitchCommandPort
    {
        public ParsedCommand? LastCommand { get; private set; }

        public ParsedCommand? LastPrepared { get; private set; }

        public Task<ParsedCommand> PrepareAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default)
        {
            LastPrepared = command;
            return Task.FromResult(command with
            {
                Argument = "Точная категория",
                ArgumentId = "game-1"
            });
        }

        public Task<string?> ExecuteAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult<string?>("готово");
        }
    }
}
