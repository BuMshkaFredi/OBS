namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>Ожидаемая ошибка Twitch с безопасным для пользователя текстом.</summary>
public class TwitchApiException : Exception
{
    public TwitchApiException(string message)
        : base(message)
    {
    }
}

/// <summary>Twitch ещё не подключён или сохранённых токенов нет.</summary>
public sealed class TwitchNotConnectedException : TwitchApiException
{
    public TwitchNotConnectedException(string message)
        : base(message)
    {
    }
}

/// <summary>Токен истёк, отозван или не содержит нужного scope.</summary>
public sealed class TwitchAuthorizationException : TwitchApiException
{
    public TwitchAuthorizationException(string message)
        : base(message)
    {
    }
}
