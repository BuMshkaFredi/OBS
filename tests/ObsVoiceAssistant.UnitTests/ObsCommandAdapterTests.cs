using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.UnitTests;

public sealed class ObsCommandAdapterTests
{
    [Fact]
    public async Task MuteInput_UsesConfiguredInputName()
    {
        var client = new FakeObsClient();
        var resolver = new ConfiguredObsInputResolver(new ObsAudioInputs(Microphone: "Mic/Aux"));
        var adapter = new ObsCommandAdapter(client, resolver);

        await adapter.ExecuteAsync(new ParsedCommand(CommandIntent.MuteInput, Target: "microphone"));

        Assert.Equal(("Mic/Aux", true), client.LastMute);
    }

    [Fact]
    public async Task MuteInputWithoutConfiguredInput_Throws()
    {
        var adapter = new ObsCommandAdapter(new FakeObsClient(), new ConfiguredObsInputResolver(new ObsAudioInputs()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.ExecuteAsync(new ParsedCommand(CommandIntent.MuteInput, Target: "microphone")));

        // Сообщение должно подсказывать, где настроить источник, иначе ошибку невозможно исправить.
        Assert.Contains("не настроен", exception.Message);
        Assert.Contains("микрофона", exception.Message);
    }

    [Fact]
    public async Task TogglePersonalMusicWithoutConfiguredInput_ThrowsHumanReadableError()
    {
        var adapter = new ObsCommandAdapter(new FakeObsClient(), new ConfiguredObsInputResolver(new ObsAudioInputs()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.ExecuteAsync(new ParsedCommand(CommandIntent.ToggleInput, Target: "personal_music")));

        // Технический код personal_music не должен попадать в уведомление пользователю.
        Assert.Contains("личной музыки", exception.Message);
        Assert.DoesNotContain("personal_music", exception.Message);
    }

    [Fact]
    public async Task ToggleInput_WhenSourceIsOn_TurnsItOffAndReports()
    {
        var client = new FakeObsClient { Muted = false };
        var adapter = new ObsCommandAdapter(client, new ConfiguredObsInputResolver(new ObsAudioInputs(Microphone: "Mic/Aux")));

        var message = await adapter.ExecuteAsync(new ParsedCommand(CommandIntent.ToggleInput, Target: "microphone"));

        Assert.Equal("Mic/Aux", client.LastToggle);
        Assert.True(client.Muted);
        Assert.Equal("Микрофон выключен", message);
    }

    [Fact]
    public async Task ToggleInput_WhenSourceIsOff_TurnsItOnAndReports()
    {
        var client = new FakeObsClient { Muted = true };
        var adapter = new ObsCommandAdapter(client, new ConfiguredObsInputResolver(new ObsAudioInputs(Microphone: "Mic/Aux")));

        var message = await adapter.ExecuteAsync(new ParsedCommand(CommandIntent.ToggleInput, Target: "microphone"));

        Assert.False(client.Muted);
        Assert.Equal("Микрофон включён", message);
    }

    [Fact]
    public async Task TogglePersonalMusic_ReportsActualState()
    {
        var client = new FakeObsClient { Muted = false };
        var adapter = new ObsCommandAdapter(client, new ConfiguredObsInputResolver(
            new ObsAudioInputs(PersonalMusic: "Личная музыка")));

        var message = await adapter.ExecuteAsync(new ParsedCommand(CommandIntent.ToggleInput, Target: "personal_music"));

        Assert.Equal("Личная музыка", client.LastToggle);
        Assert.Equal("Личная музыка выключена", message);
    }

    [Fact]
    public async Task TogglePersonalMusic_WhenSourceIsOff_TurnsItOnAndReports()
    {
        var client = new FakeObsClient { Muted = true };
        var adapter = new ObsCommandAdapter(client, new ConfiguredObsInputResolver(
            new ObsAudioInputs(PersonalMusic: "Личная музыка")));

        var message = await adapter.ExecuteAsync(new ParsedCommand(CommandIntent.ToggleInput, Target: "personal_music"));

        Assert.Equal("Личная музыка включена", message);
    }

    [Fact]
    public async Task MuteAndUnmuteStreamMusic_UseConfiguredInputName()
    {
        var client = new FakeObsClient();
        var adapter = new ObsCommandAdapter(client, new ConfiguredObsInputResolver(
            new ObsAudioInputs(StreamMusic: "Звук раб. стола")));

        await adapter.ExecuteAsync(new ParsedCommand(CommandIntent.MuteInput, Target: "stream_music"));
        Assert.Equal(("Звук раб. стола", true), client.LastMute);

        await adapter.ExecuteAsync(new ParsedCommand(CommandIntent.UnmuteInput, Target: "stream_music"));
        Assert.Equal(("Звук раб. стола", false), client.LastMute);
    }

    [Fact]
    public async Task ToggleInputWithoutConfiguredInput_Throws()
    {
        var adapter = new ObsCommandAdapter(new FakeObsClient(), new ConfiguredObsInputResolver(new ObsAudioInputs()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.ExecuteAsync(new ParsedCommand(CommandIntent.ToggleInput, Target: "microphone")));

        Assert.Contains("не настроен", exception.Message);
    }

    private sealed class FakeObsClient : IObsClient
    {
        public bool IsConnected => true;
        public bool IsReplayBufferActive => false;
        public bool Muted { get; set; }
        public (string Name, bool Muted)? LastMute { get; private set; }
        public string? LastToggle { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task StartRecordingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopRecordingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartStreamAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopStreamAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartReplayBufferAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveReplayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetInputMuteAsync(string inputName, bool muted, CancellationToken cancellationToken = default)
        {
            LastMute = (inputName, muted);
            Muted = muted;
            return Task.CompletedTask;
        }

        public Task<bool?> ToggleInputMuteAsync(string inputName, CancellationToken cancellationToken = default)
        {
            LastToggle = inputName;
            Muted = !Muted;
            return Task.FromResult<bool?>(Muted);
        }

        public Task<IReadOnlyList<string>> GetInputNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
