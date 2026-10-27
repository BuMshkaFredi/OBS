namespace ObsVoiceAssistant.Overlay;

/// <summary>Состояние данных о зрителях. Ноль не показывается как факт при ошибке.</summary>
public enum ViewerCountState
{
    Live,
    Offline,
    NotConnected,
    Unavailable
}

public sealed record ViewerCountResult(ViewerCountState State, int Viewers = 0)
{
    public static ViewerCountResult Live(int viewers) => new(ViewerCountState.Live, viewers);

    public static ViewerCountResult Offline { get; } = new(ViewerCountState.Offline);

    public static ViewerCountResult NotConnected { get; } = new(ViewerCountState.NotConnected);

    public static ViewerCountResult Unavailable { get; } = new(ViewerCountState.Unavailable);
}

/// <summary>
/// Источник числа зрителей. Окно оверлея не ходит в Twitch напрямую:
/// данные приходят через этот порт.
/// </summary>
public interface IViewerCountProvider
{
    Task<ViewerCountResult> GetViewerCountAsync(CancellationToken cancellationToken = default);
}
