namespace ObsVoiceAssistant.Integrations.Obs;

public sealed class ObsConnectionService
{
    private readonly IObsClient client;
    private readonly TimeSpan retryDelay;

    // Replay Buffer, который включило приложение. Если он пропал без нашей команды,
    // значит пользователь выключил его вручную и навязчиво включать его снова нельзя.
    private bool replayBufferExpected;
    private bool replayBufferDisabledByUser;

    public event Action<bool>? ConnectionStateChanged;

    public ObsConnectionService(IObsClient client, TimeSpan? retryDelay = null)
    {
        this.client = client;
        this.retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndEnsureReplayBufferAsync(cancellationToken).ConfigureAwait(false);
                ConnectionStateChanged?.Invoke(true);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                ConnectionStateChanged?.Invoke(false);
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ConnectAndEnsureReplayBufferAsync(CancellationToken cancellationToken = default)
    {
        // Разрыв связи и ручное выключение — разные вещи: после разрыва OBS гасит
        // Replay Buffer сам, и при переподключении его нужно снова включить.
        var reconnected = !client.IsConnected;

        while (!client.IsConnected)
        {
            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        if (reconnected)
        {
            replayBufferExpected = false;
        }

        if (client.IsReplayBufferActive)
        {
            // Пользователь включил его сам — это снимает запрет на автовключение.
            replayBufferExpected = true;
            replayBufferDisabledByUser = false;
            return;
        }

        if (replayBufferExpected)
        {
            replayBufferExpected = false;
            replayBufferDisabledByUser = true;
            return;
        }

        if (replayBufferDisabledByUser)
        {
            return;
        }

        await client.StartReplayBufferAsync(cancellationToken).ConfigureAwait(false);
        replayBufferExpected = true;
    }
}
