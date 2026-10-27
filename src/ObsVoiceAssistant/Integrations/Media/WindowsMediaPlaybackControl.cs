using ObsVoiceAssistant.Commands;
using Windows.Media.Control;

namespace ObsVoiceAssistant.Integrations.Media;

/// <summary>
/// Управляет Яндекс Музыкой через Windows Media Control. Сессия ищется по приложению,
/// а не по «текущей активной»: во время игры активной может быть совсем другая сессия.
/// Ошибки WinRT превращаются в результат, чтобы команда OBS не отменялась.
/// </summary>
public sealed class WindowsMediaPlaybackControl : IMediaPlaybackControl
{
    public Task<MediaPlaybackResult> PauseAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async session =>
            {
                var paused = await session.TryPauseAsync();
                return paused
                    ? MediaPlaybackResult.Success()
                    : MediaPlaybackResult.Failed("Windows не приняла команду паузы.");
            },
            cancellationToken);

    public Task<MediaPlaybackResult> NextTrackAsync(CancellationToken cancellationToken = default) =>
        RunAsync(
            async session =>
            {
                var skipped = await session.TrySkipNextAsync();
                return skipped
                    ? MediaPlaybackResult.Success()
                    : MediaPlaybackResult.Failed("Windows не приняла команду переключения трека.");
            },
            cancellationToken);

    private static Task<MediaPlaybackResult> RunAsync(
        Func<GlobalSystemMediaTransportControlsSession, Task<MediaPlaybackResult>> action,
        CancellationToken cancellationToken) =>
        OnUiThreadAsync(
            async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                GlobalSystemMediaTransportControlsSessionManager manager;
                try
                {
                    manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                }
                catch (Exception exception)
                {
                    return MediaPlaybackResult.Failed(Describe(exception));
                }

                var sessions = manager.GetSessions();
                var infos = sessions
                    .Select(session => new MediaSessionInfo(
                        session.SourceAppUserModelId ?? string.Empty,
                        IsPlaying(session)))
                    .ToArray();

                var index = MediaSessionSelector.SelectIndex(infos);
                if (index < 0)
                {
                    return MediaPlaybackResult.PlayerNotFound();
                }

                try
                {
                    return await action(sessions[index]);
                }
                catch (Exception exception)
                {
                    return MediaPlaybackResult.Failed(Describe(exception));
                }
            },
            cancellationToken);

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo()?.PlaybackStatus ==
                   GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch
        {
            // Сессия могла закрыться между перечислением и чтением состояния.
            return false;
        }
    }

    /// <summary>
    /// WinRT-менеджер медиа-сессий рассчитан на поток с оконным циклом. Команды
    /// приходят из фонового распознавания, поэтому вызов переносится в UI-поток.
    /// </summary>
    private static async Task<MediaPlaybackResult> OnUiThreadAsync(
        Func<Task<MediaPlaybackResult>> action,
        CancellationToken cancellationToken)
    {
        var application = System.Windows.Application.Current;
        if (application is null || application.Dispatcher.CheckAccess())
        {
            return await action().ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await application.Dispatcher
            .InvokeAsync(action)
            .Task
            .Unwrap()
            .ConfigureAwait(false);
    }

    private static string Describe(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
}
