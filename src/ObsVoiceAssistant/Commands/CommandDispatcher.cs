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
        command.Intent is CommandIntent.SetStreamCategory
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

        if (command.Intent is CommandIntent.SetStreamCategory)
        {
            return await twitch.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }

        // Плеер управляется только вместе с фактическим переключением личной музыки:
        // при выключении ставим паузу, при включении снимаем с паузы.
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
        var prefix = toggle.Message ?? "Личная музыка переключена";
        if (toggle.Muted is null)
        {
            return toggle.Message;
        }

        var playback = toggle.Muted == true
            ? await mediaPlayback!.PauseAsync(cancellationToken).ConfigureAwait(false)
            : await mediaPlayback!.ResumeAsync(cancellationToken).ConfigureAwait(false);

        // Слово в команде не предсказывает результат («выключи» могло включить),
        // поэтому текст плейера выбирается по фактическому состоянию источника.
        var verb = toggle.Muted == true ? "поставлена на паузу" : "продолжает играть";

        return playback.Outcome switch
        {
            MediaPlaybackOutcome.Succeeded => $"{prefix}. Яндекс Музыка {verb}",
            MediaPlaybackOutcome.PlayerNotFound =>
                $"{prefix}. Плеер Яндекс Музыки не найден: " +
                (toggle.Muted == true ? "пауза не выполнена" : "воспроизведение не возобновлено"),
            _ => toggle.Muted == true
                ? $"{prefix}. Не удалось поставить паузу в Яндекс Музыке{Describe(playback)}"
                : $"{prefix}. Не удалось возобновить Яндекс Музыку{Describe(playback)}"
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
