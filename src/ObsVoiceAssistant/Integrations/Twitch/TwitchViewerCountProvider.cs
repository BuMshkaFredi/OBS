using ObsVoiceAssistant.Overlay;

namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>
/// Читает число зрителей через Helix. Отсутствие токена — не сетевая ошибка:
/// опрос должен остановиться до переподключения, а не повторяться бесконечно.
/// </summary>
public sealed class TwitchViewerCountProvider : IViewerCountProvider
{
    private readonly ITwitchApiClient api;

    public TwitchViewerCountProvider(ITwitchApiClient api)
    {
        this.api = api;
    }

    public async Task<ViewerCountResult> GetViewerCountAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await api.GetStreamStatusAsync(cancellationToken).ConfigureAwait(false);
            return status.IsLive
                ? ViewerCountResult.Live(status.Viewers)
                : ViewerCountResult.Offline;
        }
        catch (TwitchNotConnectedException)
        {
            return ViewerCountResult.NotConnected;
        }
        catch (TwitchAuthorizationException)
        {
            return ViewerCountResult.NotConnected;
        }
    }
}
