using System.IO;
using System.Text.Json;
using ObsVoiceAssistant.Integrations.Obs;
using ObsVoiceAssistant.Security;

namespace ObsVoiceAssistant.Configuration;

public sealed record AppSettings(
    string ObsUrl = "ws://127.0.0.1:4455")
{
    /// <summary>
    /// Имена OBS-источников для команд mute/unmute. Задаются в окне настройки OBS,
    /// потому что имена источников у каждого пользователя свои.
    /// </summary>
    public ObsAudioInputs AudioInputs { get; init; } = new();

    /// <summary>
    /// Client ID приложения Twitch. Это публичный идентификатор, не секрет.
    /// Токены хранятся отдельно, в защищённом хранилище Windows.
    /// </summary>
    public string? TwitchClientId { get; init; }

    /// <summary>
    /// Создавать MP4-копию записи и повтора. Файл не перекодируется: потоки
    /// копируются как есть, поэтому качество не меняется.
    /// </summary>
    public bool CreateMp4Copy { get; init; } = true;

    /// <summary>
    /// Путь к ffmpeg.exe. Если пусто, программа ищется в папке ffmpeg рядом
    /// с приложением и в PATH.
    /// </summary>
    public string? FfmpegPath { get; init; }

    /// <summary>
    /// Показывать окно со счётчиком зрителей. По умолчанию выключено:
    /// окно поверх всех окон не должно появляться без решения пользователя.
    /// </summary>
    public bool ViewerOverlayEnabled { get; init; }

    /// <summary>Интервал опроса Helix в секундах. Меньше 15 секунд не используется.</summary>
    public int ViewerOverlayIntervalSeconds { get; init; } = 30;

    /// <summary>Сохранённая позиция окна. <c>null</c> — правый верхний угол.</summary>
    public double? ViewerOverlayLeft { get; init; }

    /// <summary>Сохранённая позиция окна. <c>null</c> — правый верхний угол.</summary>
    public double? ViewerOverlayTop { get; init; }
}

public sealed class SettingsService
{
    private const string ObsPasswordKey = "obs.websocket.password";
    private readonly string settingsPath;
    private readonly DpapiSecretStore secretStore;

    public SettingsService(string? dataDirectory = null, DpapiSecretStore? secretStore = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ObsVoiceAssistant");
        Directory.CreateDirectory(directory);
        settingsPath = Path.Combine(directory, "settings.json");
        this.secretStore = secretStore ?? new DpapiSecretStore(Path.Combine(directory, "secrets"));
    }

    public AppSettings Load()
    {
        if (!File.Exists(settingsPath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, json);
    }

    public void SaveObsPassword(string password) => secretStore.Save(ObsPasswordKey, password);

    public string? LoadObsPassword() => secretStore.Load(ObsPasswordKey);

    public void DeleteObsPassword() => secretStore.Delete(ObsPasswordKey);
}
