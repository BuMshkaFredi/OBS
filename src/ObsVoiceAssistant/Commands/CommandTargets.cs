namespace ObsVoiceAssistant.Commands;

/// <summary>
/// Человеческие названия целей звуковых команд. Нужны в уведомлениях и ошибках,
/// чтобы пользователь не видел технические коды вроде <c>personal_music</c>.
/// </summary>
public static class CommandTargets
{
    /// <summary>Винительный падеж: «выключить личную музыку».</summary>
    public static string Accusative(string? target) => target switch
    {
        "microphone" => "микрофон",
        "browser" => "браузер",
        "personal_music" => "личную музыку",
        "stream_music" => "музыку стрима",
        _ => target ?? "источник"
    };

    /// <summary>Родительный падеж: «источник для личной музыки».</summary>
    public static string Genitive(string? target) => target switch
    {
        "microphone" => "микрофона",
        "browser" => "браузера",
        "personal_music" => "личной музыки",
        "stream_music" => "музыки стрима",
        _ => target ?? "источника"
    };
}
