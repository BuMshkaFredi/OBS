namespace ObsVoiceAssistant.Overlay;

/// <summary>
/// Периодически читает число зрителей. Опрос останавливается, когда Twitch
/// не подключён, и возобновляется после переподключения. Сетевые ошибки
/// не показываются уведомлениями: окно просто приглушает последнее значение.
/// </summary>
public sealed class ViewerCountPollingService : IAsyncDisposable
{
    private readonly IViewerCountProvider provider;
    private readonly Func<TimeSpan> intervalProvider;
    private readonly Action<string>? diagnostic;
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim resumeSignal = new(0, 1);

    private Task? loop;
    private volatile bool paused;
    private bool disposed;

    public ViewerCountPollingService(
        IViewerCountProvider provider,
        Func<TimeSpan> intervalProvider,
        Action<string>? diagnostic = null)
    {
        this.provider = provider;
        this.intervalProvider = intervalProvider;
        this.diagnostic = diagnostic;
    }

    public event Action<ViewerCountResult>? Updated;

    public void Start()
    {
        ThrowIfDisposed();
        if (loop is not null)
        {
            return;
        }

        paused = false;
        loop = Task.Run(() => RunAsync(stopping.Token));
    }

    /// <summary>Останавливает сетевые запросы, не завершая службу.</summary>
    public void Pause() => paused = true;

    public void Resume()
    {
        if (disposed)
        {
            return;
        }

        paused = false;
        Wake();
    }

    public async Task StopAsync()
    {
        if (disposed)
        {
            return;
        }

        await stopping.CancelAsync().ConfigureAwait(false);
        Wake();
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Остановка — ожидаемый путь, а не ошибка.
            }
        }

        loop = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (paused)
            {
                try
                {
                    await resumeSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            ViewerCountResult result;
            try
            {
                result = await provider.GetViewerCountAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                diagnostic?.Invoke(
                    $"Оверлей зрителей: {exception.GetType().Name}: {exception.Message}");
                result = ViewerCountResult.Unavailable;
            }

            Publish(result);

            // Без токенов или с отозванным токеном опрос бессмысленен: ждём,
            // пока пользователь подключит Twitch заново.
            if (result.State == ViewerCountState.NotConnected)
            {
                paused = true;
                continue;
            }

            try
            {
                await Task.Delay(intervalProvider(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Publish(ViewerCountResult result)
    {
        try
        {
            Updated?.Invoke(result);
        }
        catch (Exception exception)
        {
            diagnostic?.Invoke($"Оверлей зрителей: ошибка обновления окна: {exception.Message}");
        }
    }

    private void Wake()
    {
        try
        {
            if (resumeSignal.CurrentCount == 0)
            {
                resumeSignal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
            // Будить уже некого: следующий цикл сам проверит состояние.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        disposed = true;
        stopping.Dispose();
        resumeSignal.Dispose();
    }
}
