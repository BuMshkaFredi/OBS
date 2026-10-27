namespace ObsVoiceAssistant.Commands;

public sealed class CommandDispatcher
{
    private readonly IObsCommandPort obs;
    private readonly ITwitchCommandPort twitch;
    private readonly IConfirmationPolicy confirmationPolicy;
    private readonly IMediaPlaybackControl? mediaPlayback;

    public CommandDispatcher(
        IObsCommandPort obs,
        ITwitchCommandPort twitch,
        IConfirmationPolicy confirmationPolicy,
        IMediaPlaybackControl? mediaPlayback = null)
    {
        this.obs = obs;
        this.twitch = twitch;
        this.confirmationPolicy = confirmationPolicy;
        this.mediaPlayback = mediaPlayback;
    }

    public bool RequiresConfirmation(ParsedCommand command) =>
        confirmationPolicy.RequiresConfirmation(command.Intent);

    /// <summary>
    /// Выполняет проверку до подтверждения. Сейчас она нужна категории Twitch:
    /// пользователь должен увидеть в уведомлении реальное название с Twitch.
    /// </summary>
    public Task<ParsedCommand> PrepareAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default) =>
        command.Intent is CommandIntent.SetStreamTitle or CommandIntent.SetStreamCategory
            ? twitch.PrepareAsync(command, cancellationToken)
            : Task.FromResult(command);

    public async Task<string?> DispatchAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Intent == CommandIntent.NextMusicTrack)
        {
            return await NextMusicTrackAsync(cancellationToken).ConfigureAwait(false);
        }

        if (command.Intent is CommandIntent.SetStreamTitle or CommandIntent.SetStreamCategory)
        {
            return await twitch.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }

        // Пауза имеет смысл только после фактического выключения личной музыки.
        // Если источник включили, плеер не трогаем: возобновление остаётся за пользователем.
        if (command.Intent == CommandIntent.ToggleInput &&
            command.Target == "personal_music" &&
            mediaPlayback is not null)
        {
            return await TogglePersonalMusicAsync(command, cancellationToken).ConfigureAwait(false);
        }

        return await obs.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TogglePersonalMusicAsync(
        ParsedCommand command,
        CancellationToken cancellationToken)
    {
        var toggle = await obs.ToggleInputAsync(command, cancellationToken).ConfigureAwait(false);
        if (toggle.Muted != true)
        {
            return toggle.Message;
        }

        var playback = await mediaPlayback!.PauseAsync(cancellationToken).ConfigureAwait(false);
        var prefix = toggle.Message ?? "Личная музыка переключена";

        return playback.Outcome switch
        {
            MediaPlaybackOutcome.Succeeded => $"{prefix}. Яндекс Музыка на паузе",
            MediaPlaybackOutcome.PlayerNotFound =>
                $"{prefix}. Плеер Яндекс Музыки не найден: пауза не выполнена",
            _ => $"{prefix}. Не удалось поставить паузу в Яндекс Музыке{Describe(playback)}"
        };
    }

    private async Task<string> NextMusicTrackAsync(CancellationToken cancellationToken)
    {
        if (mediaPlayback is null)
        {
            throw new InvalidOperationException("Управление Яндекс Музыкой не подключено.");
        }

        var playback = await mediaPlayback.NextTrackAsync(cancellationToken).ConfigureAwait(false);

        return playback.Outcome switch
        {
            MediaPlaybackOutcome.Succeeded => "Трек переключён в Яндекс Музыке",
            MediaPlaybackOutcome.PlayerNotFound =>
                "Плеер Яндекс Музыки не найден: трек не переключён",
            _ => $"Не удалось переключить трек{Describe(playback)}"
        };
    }

    private static string Describe(MediaPlaybackResult result) =>
        string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : $": {result.Detail}";
}
