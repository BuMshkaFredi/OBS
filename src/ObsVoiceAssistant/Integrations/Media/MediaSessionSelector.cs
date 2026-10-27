namespace ObsVoiceAssistant.Integrations.Media;

/// <summary>Минимальные сведения о медиа-сессии Windows, нужные для выбора плеера.</summary>
public readonly record struct MediaSessionInfo(string AppId, bool IsPlaying);

/// <summary>
/// Выбирает сессию Яндекс Музыки среди всех медиа-сессий. Логика отделена от WinRT,
/// чтобы правило выбора проверялось unit-тестами без запущенного плеера.
/// </summary>
public static class MediaSessionSelector
{
    private const string YandexMarker = "yandex";
    private const string YandexCyrillicMarker = "яндекс";

    /// <summary>
    /// Индекс подходящей сессии или -1, если Яндекс Музыка не публикует медиа-сессию.
    /// Предпочитается играющая сессия: у приложения может остаться и старая пауза.
    /// </summary>
    public static int SelectIndex(IReadOnlyList<MediaSessionInfo> sessions)
    {
        var pausedIndex = -1;

        for (var index = 0; index < sessions.Count; index++)
        {
            if (!IsYandex(sessions[index].AppId))
            {
                continue;
            }

            if (sessions[index].IsPlaying)
            {
                return index;
            }

            if (pausedIndex < 0)
            {
                pausedIndex = index;
            }
        }

        return pausedIndex;
    }

    /// <summary>
    /// AUMID приложения содержит «yandex» или «яндекс»: так находятся и магазинная
    /// версия, и обычная установка. Точное имя пакета у разных сборок отличается.
    /// </summary>
    public static bool IsYandex(string? appId) =>
        !string.IsNullOrWhiteSpace(appId) &&
        (appId.Contains(YandexMarker, StringComparison.OrdinalIgnoreCase) ||
         appId.Contains(YandexCyrillicMarker, StringComparison.OrdinalIgnoreCase));
}
