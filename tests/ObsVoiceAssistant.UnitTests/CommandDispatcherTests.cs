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
        var command = new ParsedCommand(CommandIntent.SetStreamCategory, "Dota 2");

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

    [Fact]
    public async Task PersonalMusicMute_PausesYandexMusic()
    {
        var obs = new RecordingObsPort
        {
            ToggleResult = new InputToggleResult("Личная музыка выключена", true)
        };
        var media = new RecordingMediaPlayback();
        var dispatcher = new CommandDispatcher(
            obs,
            new NullTwitchCommandPort(),
            new ConfirmationPolicy(),
            media);

        var outcome = await dispatcher.DispatchAsync(
            new ParsedCommand(CommandIntent.ToggleInput, Target: "personal_music"));

        Assert.Equal(1, media.PauseCalls);
        Assert.Contains("Яндекс Музыка поставлена на паузу", outcome, StringComparison.Ordinal);
        Assert.False(obs.ExecuteCalled);
    }

    [Fact]
    public async Task PersonalMusicUnmute_ResumesYandexMusic()
    {
        var obs = new RecordingObsPort
        {
            ToggleResult = new InputToggleResult("Личная музыка включена", false)
        };
        var media = new RecordingMediaPlayback();
        var dispatcher = new CommandDispatcher(
            obs,
            new NullTwitchCommandPort(),
            new ConfirmationPolicy(),
            media);

        var outcome = await dispatcher.DispatchAsync(
            new ParsedCommand(CommandIntent.ToggleInput, Target: "personal_music"));

        Assert.Equal(0, media.PauseCalls);
        Assert.Equal(1, media.ResumeCalls);
        Assert.Contains("Личная музыка включена", outcome, StringComparison.Ordinal);
        Assert.Contains("продолжает играть", outcome, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersonalMusicUnmute_WithoutPlayer_StillReportsObsResult()
    {
        var obs = new RecordingObsPort
        {
            ToggleResult = new InputToggleResult("Личная музыка включена", false)
        };
        var media = new RecordingMediaPlayback
        {
            ResumeResult = MediaPlaybackResult.PlayerNotFound()
        };
        var dispatcher = new CommandDispatcher(
            obs,
            new NullTwitchCommandPort(),
            new ConfirmationPolicy(),
            media);

        var outcome = await dispatcher.DispatchAsync(
            new ParsedCommand(CommandIntent.ToggleInput, Target: "personal_music"));

        Assert.Contains("Личная музыка включена", outcome, StringComparison.Ordinal);
        Assert.Contains("не найден", outcome, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersonalMusicMute_WithoutPlayer_StillReportsObsResult()
    {
        var obs = new RecordingObsPort
        {
            ToggleResult = new InputToggleResult("Личная музыка выключена", true)
        };
        var media = new RecordingMediaPlayback
        {
            PauseResult = MediaPlaybackResult.PlayerNotFound()
        };
        var dispatcher = new CommandDispatcher(
            obs,
            new NullTwitchCommandPort(),
            new ConfirmationPolicy(),
            media);

        var outcome = await dispatcher.DispatchAsync(
            new ParsedCommand(CommandIntent.ToggleInput, Target: "personal_music"));

        Assert.Contains("Личная музыка выключена", outcome, StringComparison.Ordinal);
        Assert.Contains("не найден", outcome, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NextMusicTrack_CallsMediaPlayer()
    {
        var media = new RecordingMediaPlayback();
        var dispatcher = new CommandDispatcher(
            new RecordingObsPort(),
            new NullTwitchCommandPort(),
            new ConfirmationPolicy(),
            media);

        var outcome = await dispatcher.DispatchAsync(
            new ParsedCommand(CommandIntent.NextMusicTrack));

        Assert.Equal(1, media.NextTrackCalls);
        Assert.Contains("переключён", outcome, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NextMusicTrack_WithoutPlayer_DoesNotThrow()
    {
        var media = new RecordingMediaPlayback
        {
            NextTrackResult = MediaPlaybackResult.PlayerNotFound()
        };
        var dispatcher = new CommandDispatcher(
            new RecordingObsPort(),
            new NullTwitchCommandPort(),
            new ConfirmationPolicy(),
            media);

        var outcome = await dispatcher.DispatchAsync(
            new ParsedCommand(CommandIntent.NextMusicTrack));

        Assert.Contains("не найден", outcome, StringComparison.Ordinal);
    }

    private sealed class RecordingObsPort : IObsCommandPort
    {
        public ParsedCommand? LastCommand { get; private set; }

        public bool ExecuteCalled { get; private set; }

        public InputToggleResult ToggleResult { get; init; } = new(null, null);

        public Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default)
        {
            ExecuteCalled = true;
            LastCommand = command;
            return Task.FromResult<string?>(null);
        }

        public Task<InputToggleResult> ToggleInputAsync(
            ParsedCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(ToggleResult);
        }
    }

    private sealed class RecordingMediaPlayback : IMediaPlaybackControl
    {
        public int PauseCalls { get; private set; }

        public int ResumeCalls { get; private set; }

        public int NextTrackCalls { get; private set; }

        public MediaPlaybackResult PauseResult { get; init; } = MediaPlaybackResult.Success();

        public MediaPlaybackResult ResumeResult { get; init; } = MediaPlaybackResult.Success();

        public MediaPlaybackResult NextTrackResult { get; init; } = MediaPlaybackResult.Success();

        public Task<MediaPlaybackResult> PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCalls++;
            return Task.FromResult(PauseResult);
        }

        public Task<MediaPlaybackResult> ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCalls++;
            return Task.FromResult(ResumeResult);
        }

        public Task<MediaPlaybackResult> NextTrackAsync(CancellationToken cancellationToken = default)
        {
            NextTrackCalls++;
            return Task.FromResult(NextTrackResult);
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
