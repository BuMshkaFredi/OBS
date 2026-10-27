using System.Text.Json;
using ObsVoiceAssistant.Security;

namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>
/// Хранит токены Twitch только в защищённом хранилище Windows (DPAPI).
/// В settings.json попадает лишь публичный Client ID.
/// </summary>
public sealed class TwitchTokenStore
{
    private const string Key = "twitch.tokens";
    private readonly IProtectedSecretStore secrets;

    public TwitchTokenStore(IProtectedSecretStore secrets)
    {
        this.secrets = secrets;
    }

    public TwitchTokens? Load()
    {
        var json = secrets.Load(Key);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TwitchTokens>(json);
        }
        catch (JsonException)
        {
            // Повреждённый файл не должен ронять приложение: считаем, что подключения нет.
            return null;
        }
    }

    public void Save(TwitchTokens tokens) =>
        secrets.Save(Key, JsonSerializer.Serialize(tokens));

    public void Delete() => secrets.Delete(Key);

    public bool Exists()
    {
        try
        {
            return Load() is not null;
        }
        catch
        {
            // Ошибка DPAPI (например, профиль другого пользователя) означает,
            // что использовать сохранённый токен нельзя.
            return false;
        }
    }
}
