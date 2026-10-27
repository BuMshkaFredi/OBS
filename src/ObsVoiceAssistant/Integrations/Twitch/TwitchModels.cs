namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>Код устройства, который пользователь вводит на странице Twitch.</summary>
public sealed record TwitchDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int IntervalSeconds);

/// <summary>Пара токенов Twitch. Refresh token публичного клиента одноразовый.</summary>
public sealed record TwitchTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<string> Scopes);

/// <summary>Результат проверки access token через /oauth2/validate.</summary>
public sealed record TwitchValidation(
    string ClientId,
    string UserId,
    string Login,
    IReadOnlyList<string> Scopes,
    int ExpiresInSeconds);

public sealed record TwitchUser(string Id, string Login, string DisplayName);

public sealed record TwitchCategory(string Id, string Name, string? BoxArtUrl);

/// <summary>Текущие данные канала: название нужно для сохранения тегов.</summary>
public sealed record TwitchChannel(string Title, string GameName, string GameId);
