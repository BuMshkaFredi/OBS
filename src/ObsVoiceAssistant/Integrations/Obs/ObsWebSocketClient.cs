using OBSWebsocketDotNet;

namespace ObsVoiceAssistant.Integrations.Obs;

public sealed class ObsWebSocketClient : IObsClient, IDisposable
{
    private readonly OBSWebsocket client = new();
    private readonly ObsConnectionOptions options;
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private readonly TimeSpan identificationTimeout = TimeSpan.FromSeconds(5);

    public ObsWebSocketClient(ObsConnectionOptions options)
    {
        this.options = options;
    }

    public bool IsConnected => client.IsIdentified;

    public bool IsReplayBufferActive => IsConnected && client.GetReplayBufferStatus();

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (client.IsIdentified)
            {
                return;
            }

            if (client.IsConnected)
            {
                await ExecuteAsync(client.Disconnect, cancellationToken).ConfigureAwait(false);
            }

            await ExecuteAsync(() => client.ConnectAsync(options.Url, options.Password ?? string.Empty), cancellationToken).ConfigureAwait(false);
            var deadline = DateTime.UtcNow + identificationTimeout;
            while (!client.IsIdentified && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            if (!client.IsIdentified)
            {
                await ExecuteAsync(client.Disconnect, CancellationToken.None).ConfigureAwait(false);
                throw new TimeoutException("OBS WebSocket did not complete identification within the allowed time.");
            }
        }
        finally
        {
            connectionLock.Release();
        }
    }

    public Task DisconnectAsync() =>
        ExecuteAsync(client.Disconnect, CancellationToken.None);

    public Task StartRecordingAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(client.StartRecord, cancellationToken);

    public Task StopRecordingAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => _ = client.StopRecord(), cancellationToken);

    public Task StartStreamAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(client.StartStream, cancellationToken);

    public Task StopStreamAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(client.StopStream, cancellationToken);

    public Task StartReplayBufferAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(client.StartReplayBuffer, cancellationToken);

    public Task SaveReplayAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(client.SaveReplayBuffer, cancellationToken);

    public Task SetInputMuteAsync(string inputName, bool muted, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => client.SetInputMute(inputName, muted), cancellationToken);

    public Task<bool?> ToggleInputMuteAsync(string inputName, CancellationToken cancellationToken = default) =>
        ExecuteAsync<bool?>(
            () =>
            {
                // Переключает сам OBS, поэтому гонки «прочитал — записал» не возникает.
                client.ToggleInputMute(inputName);
                try
                {
                    return client.GetInputMute(inputName);
                }
                catch
                {
                    // Переключение уже выполнено; состояние нужно только для сообщения.
                    return null;
                }
            },
            cancellationToken);

    public Task<IReadOnlyList<string>> GetInputNamesAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<string>>(
            () => client.GetInputList()
                .Select(input => input.InputName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList(),
            cancellationToken);

    public void Dispose()
    {
        try
        {
            client.Disconnect();
        }
        catch
        {
            // Соединение могло не установиться; в этом случае отключать нечего.
        }

        connectionLock.Dispose();
    }

    private static Task ExecuteAsync(Action operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(operation, cancellationToken);
    }

    private static Task<T> ExecuteAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(operation, cancellationToken);
    }
}
