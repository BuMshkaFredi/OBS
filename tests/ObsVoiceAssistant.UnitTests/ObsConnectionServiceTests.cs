using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.UnitTests;

public sealed class ObsConnectionServiceTests
{
    [Fact]
    public async Task ConnectAndEnsureReplayBuffer_ConnectsAndStartsReplayBuffer()
    {
        var client = new FakeObsClient();
        var service = new ObsConnectionService(client, TimeSpan.Zero);

        await service.ConnectAndEnsureReplayBufferAsync();

        Assert.Equal(1, client.ConnectCalls);
        Assert.Equal(1, client.StartReplayCalls);
    }

    [Fact]
    public async Task ReplayBuffer_StoppedManually_IsNotRestarted()
    {
        var client = new FakeObsClient();
        var service = new ObsConnectionService(client, TimeSpan.Zero);

        await service.ConnectAndEnsureReplayBufferAsync();
        client.SimulateManualReplayStop();

        await service.ConnectAndEnsureReplayBufferAsync();

        Assert.Equal(1, client.StartReplayCalls);
    }

    [Fact]
    public async Task ReplayBuffer_StoppedManually_StaysOffAfterReconnect()
    {
        var client = new FakeObsClient();
        var service = new ObsConnectionService(client, TimeSpan.Zero);

        await service.ConnectAndEnsureReplayBufferAsync();
        client.SimulateManualReplayStop();
        await service.ConnectAndEnsureReplayBufferAsync();

        client.SimulateDisconnect();
        await service.ConnectAndEnsureReplayBufferAsync();

        Assert.Equal(2, client.ConnectCalls);
        Assert.Equal(1, client.StartReplayCalls);
    }

    [Fact]
    public async Task ReplayBuffer_IsRestartedAfterReconnectWithoutManualStop()
    {
        var client = new FakeObsClient();
        var service = new ObsConnectionService(client, TimeSpan.Zero);

        await service.ConnectAndEnsureReplayBufferAsync();
        client.SimulateDisconnect();

        await service.ConnectAndEnsureReplayBufferAsync();

        Assert.Equal(2, client.ConnectCalls);
        Assert.Equal(2, client.StartReplayCalls);
    }

    [Fact]
    public async Task ReplayBuffer_EnabledByUser_ClearsManualStop()
    {
        var client = new FakeObsClient();
        var service = new ObsConnectionService(client, TimeSpan.Zero);

        await service.ConnectAndEnsureReplayBufferAsync();
        client.SimulateManualReplayStop();
        await service.ConnectAndEnsureReplayBufferAsync();

        // Пользователь снова включил Replay Buffer сам: запрет на автовключение снимается.
        client.SimulateUserReplayStart();
        await service.ConnectAndEnsureReplayBufferAsync();
        client.SimulateManualReplayStop();
        await service.ConnectAndEnsureReplayBufferAsync();

        Assert.Equal(1, client.StartReplayCalls);
    }

    private sealed class FakeObsClient : IObsClient
    {
        public bool IsConnected { get; private set; }
        public bool IsReplayBufferActive { get; private set; }
        public int ConnectCalls { get; private set; }
        public int StartReplayCalls { get; private set; }

        public void SimulateManualReplayStop() => IsReplayBufferActive = false;

        public void SimulateUserReplayStart() => IsReplayBufferActive = true;

        public void SimulateDisconnect()
        {
            IsConnected = false;
            IsReplayBufferActive = false;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync() => Task.CompletedTask;
        public Task StartRecordingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopRecordingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartStreamAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopStreamAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartReplayBufferAsync(CancellationToken cancellationToken = default)
        {
            StartReplayCalls++;
            IsReplayBufferActive = true;
            return Task.CompletedTask;
        }
        public Task SaveReplayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetInputMuteAsync(string inputName, bool muted, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool?> ToggleInputMuteAsync(string inputName, CancellationToken cancellationToken = default) =>
            Task.FromResult<bool?>(null);
        public Task<float> GetInputVolumeAsync(string inputName, CancellationToken cancellationToken = default) =>
            Task.FromResult(0f);
        public Task SetInputVolumeAsync(
            string inputName,
            float volumeMultiplier,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ObsMuteAllResult> MuteAllAudioAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ObsMuteAllResult(0, Array.Empty<string>()));
        public Task<IReadOnlyList<string>> GetInputNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
