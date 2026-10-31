using ObsVoiceAssistant.Integrations.Twitch;
using ObsVoiceAssistant.Overlay;

namespace ObsVoiceAssistant.UnitTests;

public sealed class TwitchViewerCountProviderTests
{
    [Fact]
    public async Task LiveStream_ReturnsViewerCount()
    {
        var provider = new TwitchViewerCountProvider(
            new FakeApi(new TwitchStreamStatus(IsLive: true, Viewers: 321)));

        var result = await provider.GetViewerCountAsync();

        Assert.Equal(ViewerCountState.Live, result.State);
        Assert.Equal(321, result.Viewers);
    }

    [Fact]
    public async Task OfflineStream_ReturnsOffline()
    {
        var provider = new TwitchViewerCountProvider(
            new FakeApi(new TwitchStreamStatus(IsLive: false, Viewers: 0)));

        var result = await provider.GetViewerCountAsync();

        Assert.Equal(ViewerCountState.Offline, result.State);
    }

    [Theory]
    [InlineData(typeof(TwitchNotConnectedException))]
    [InlineData(typeof(TwitchAuthorizationException))]
    public async Task MissingToken_ReturnsNotConnected(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Twitch не подключён")!;
        var provider = new TwitchViewerCountProvider(new FakeApi(exception));

        var result = await provider.GetViewerCountAsync();

        Assert.Equal(ViewerCountState.NotConnected, result.State);
    }

    [Fact]
    public async Task NetworkError_IsPropagatedForStaleValue()
    {
        var provider = new TwitchViewerCountProvider(
            new FakeApi(new TwitchApiException("Twitch вернул ошибку 503.")));

        await Assert.ThrowsAsync<TwitchApiException>(() => provider.GetViewerCountAsync());
    }

    private sealed class FakeApi : ITwitchApiClient
    {
        private readonly TwitchStreamStatus? status;
        private readonly Exception? failure;

        public FakeApi(TwitchStreamStatus status)
        {
            this.status = status;
        }

        public FakeApi(Exception failure)
        {
            this.failure = failure;
        }

        public Task<TwitchUser> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TwitchCategory?> FindFirstCategoryAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TwitchChannel> GetChannelAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TwitchStreamStatus> GetStreamStatusAsync(
            CancellationToken cancellationToken = default) =>
            failure is null
                ? Task.FromResult(status!)
                : Task.FromException<TwitchStreamStatus>(failure);

        public Task UpdateCategoryAsync(string gameId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
