namespace ObsVoiceAssistant.Integrations.Obs;

/// <summary>
/// Итог команды «выключить весь звук». <see cref="MutedCount"/> считает только
/// источники, у которых действительно есть звук; видеоисточники без аудио
/// пропускаются и в счётчик не попадают.
/// </summary>
public sealed record ObsMuteAllResult(
    int MutedCount,
    IReadOnlyList<string> FailedInputs);
