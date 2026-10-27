using System.Net.Http;
using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Configuration;
using ObsVoiceAssistant.Security;

namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>
/// Точка входа Twitch-интеграции: Device Code OAuth, хранение токенов и Helix.
/// Читает Client ID из настроек при каждом обращении, поэтому смена Client ID
/// не требует пересоздавать сервис.
/// </summary>
public sealed class TwitchService : IDisposable
{
    private readonly SettingsService settings;
    private readonly TwitchTokenStore tokenStore;
    private readonly TwitchOAuthClient oauth;
    private readonly TwitchApiClient api;
    private readonly HttpClient http;

    public TwitchService(
        SettingsService settings,
        IProtectedSecretStore? secretStore = null,
        HttpMessageHandler? httpMessageHandler = null)
    {
        this.settings = settings;
        http = httpMessageHandler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) }
            : new HttpClient(httpMessageHandler) { Timeout = TimeSpan.FromSeconds(30) };

        tokenStore = new TwitchTokenStore(secretStore ?? new DpapiSecretStore());
        oauth = new TwitchOAuthClient(http);
        api = new TwitchApiClient(
            () => this.settings.Load().TwitchClientId,
            tokenStore,
            oauth,
            http);
        CommandPort = new TwitchCommandAdapter(api);
    }

    public ITwitchCommandPort CommandPort { get; }

    public bool HasClientId => !string.IsNullOrWhiteSpace(settings.Load().TwitchClientId);

    public bool HasSavedTokens => tokenStore.Exists();

    public Task<TwitchDeviceCode> BeginConnectAsync(
        CancellationToken cancellationToken = default) =>
        oauth.StartDeviceFlowAsync(RequireClientId(), cancellationToken);

    /// <summary>
    /// Дожидается подтверждения Device Code и сохраняет токены только после
    /// успешной проверки, что Twitch выдал нужный scope.
    /// </summary>
    public async Task<TwitchUser> CompleteConnectAsync(
        TwitchDeviceCode deviceCode,
        CancellationToken cancellationToken = default)
    {
        var clientId = RequireClientId();
        var tokens = await oauth
            .PollForTokenAsync(clientId, deviceCode, cancellationToken)
            .ConfigureAwait(false);

        if (!tokens.Scopes.Contains(TwitchOAuthClient.RequiredScope, StringComparer.Ordinal))
        {
            throw new TwitchAuthorizationException(
                "Twitch не выдал право channel:manage:broadcast. Подключите Twitch заново.");
        }

        tokenStore.Save(tokens);
        try
        {
            // Проверка через validate и /helix/users заодно проверяет scope.
            return await api.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            tokenStore.Delete();
            throw;
        }
    }

    public async Task<TwitchUser> CheckConnectionAsync(
        CancellationToken cancellationToken = default) =>
        await api.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Удаляет локальные токены. Отзыв на стороне Twitch — по возможности:
    /// недоступность сети не должна мешать отключению в приложении.
    /// </summary>
    public async Task DisconnectAsync(
        bool revokeOnTwitch = true,
        CancellationToken cancellationToken = default)
    {
        TwitchTokens? tokens = null;
        try
        {
            tokens = tokenStore.Load();
        }
        catch
        {
            // Даже если DPAPI не смог расшифровать файл, отключение должно
            // удалить его локально; отозвать неизвестный токен невозможно.
        }

        tokenStore.Delete();

        if (!revokeOnTwitch || tokens is null)
        {
            return;
        }

        var clientId = settings.Load().TwitchClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await oauth.RevokeAsync(clientId, tokens.AccessToken, timeout.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // Локальный токен уже удалён; ошибка отзыва не должна показываться как сбой.
        }
    }

    public void Dispose() => http.Dispose();

    private string RequireClientId()
    {
        var clientId = settings.Load().TwitchClientId?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new TwitchNotConnectedException(
                "Twitch не настроен: укажите Client ID в настройках.");
        }

        return clientId;
    }
}
