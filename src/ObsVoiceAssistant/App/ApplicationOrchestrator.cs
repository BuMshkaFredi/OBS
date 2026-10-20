using ObsVoiceAssistant.Integrations.Obs;
using ObsVoiceAssistant.Tray;

namespace ObsVoiceAssistant.Application;

public sealed class ApplicationOrchestrator : IAsyncDisposable
{
    private readonly ObsConnectionService obsConnection;
    private readonly ITrayStatus trayStatus;
    private readonly CancellationTokenSource cancellation = new();
    private Task? connectionTask;

    public ApplicationOrchestrator(ObsConnectionService obsConnection, ITrayStatus trayStatus)
    {
        this.obsConnection = obsConnection;
        this.trayStatus = trayStatus;
    }

    public void Start()
    {
        connectionTask = ConnectAsync();
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        if (connectionTask is not null)
        {
            try
            {
                await connectionTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation.Dispose();
    }

    private async Task ConnectAsync()
    {
        try
        {
            trayStatus.SetStatus("подключение...");
            obsConnection.ConnectionStateChanged += connected =>
                trayStatus.SetStatus(connected ? "подключен" : "недоступен");
            await obsConnection.RunAsync(cancellation.Token).ConfigureAwait(false);
            trayStatus.SetStatus("подключен");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch
        {
            trayStatus.SetStatus("недоступен");
        }
    }
}
