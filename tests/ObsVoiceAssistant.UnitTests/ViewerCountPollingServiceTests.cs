using ObsVoiceAssistant.Overlay;

namespace ObsVoiceAssistant.UnitTests;

public sealed class ViewerCountPollingServiceTests
{
    [Fact]
    public async Task Start_PublishesLiveViewerCount()
    {
        var provider = new ScriptedProvider(ViewerCountResult.Live(42));
        var updated = new TaskCompletionSource<ViewerCountResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = CreateService(provider, result => updated.TrySetResult(result), TimeSpan.FromMilliseconds(20));

        service.Start();

        var result = await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ViewerCountState.Live, result.State);
        Assert.Equal(42, result.Viewers);
    }

    [Fact]
    public async Task NotConnected_PausesPollingUntilResume()
    {
        var provider = new ScriptedProvider(
            ViewerCountResult.NotConnected,
            ViewerCountResult.Live(5));
        var first = new TaskCompletionSource<ViewerCountResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ViewerCountResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var updates = 0;

        await using var service = CreateService(
            provider,
            result =>
            {
                if (Interlocked.Increment(ref updates) == 1)
                {
                    first.TrySetResult(result);
                }
                else
                {
                    second.TrySetResult(result);
                }
            },
            TimeSpan.FromMilliseconds(10));

        service.Start();
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Delay(100);
        Assert.Equal(1, provider.Calls);

        service.Resume();
        var resumed = await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ViewerCountState.Live, resumed.State);
        Assert.Equal(5, resumed.Viewers);
    }

    [Fact]
    public async Task NetworkError_PublishesUnavailableAndKeepsPolling()
    {
        var provider = new FailingThenLiveProvider();
        var unavailable = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var live = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var service = CreateService(
            provider,
            result =>
            {
                if (result.State == ViewerCountState.Unavailable)
                {
                    unavailable.TrySetResult();
                }
                else if (result.State == ViewerCountState.Live)
                {
                    live.TrySetResult();
                }
            },
            TimeSpan.FromMilliseconds(10));

        service.Start();

        await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await live.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(provider.Calls >= 2);
    }

    private static ViewerCountPollingService CreateService(
        IViewerCountProvider provider,
        Action<ViewerCountResult> onUpdated,
        TimeSpan interval)
    {
        var service = new ViewerCountPollingService(provider, () => interval);
        service.Updated += onUpdated;
        return service;
    }

    private sealed class ScriptedProvider : IViewerCountProvider
    {
        private readonly Queue<ViewerCountResult> results;
        private readonly object gate = new();
        private int calls;

        public ScriptedProvider(params ViewerCountResult[] results)
        {
            this.results = new Queue<ViewerCountResult>(results);
        }

        public int Calls => Volatile.Read(ref calls);

        public Task<ViewerCountResult> GetViewerCountAsync(
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(
                    results.Count > 0 ? results.Dequeue() : ViewerCountResult.Offline);
            }
        }
    }

    private sealed class FailingThenLiveProvider : IViewerCountProvider
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task<ViewerCountResult> GetViewerCountAsync(
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref calls);
            return call == 1
                ? Task.FromException<ViewerCountResult>(new InvalidOperationException("сеть"))
                : Task.FromResult(ViewerCountResult.Live(7));
        }
    }
}
