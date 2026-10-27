using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>Минимальный набор Helix-операций, нужный голосовым командам.</summary>
public interface ITwitchApiClient
{
    Task<TwitchUser> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    Task<TwitchCategory?> FindFirstCategoryAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task UpdateTitleAsync(string title, CancellationToken cancellationToken = default);

    Task UpdateCategoryAsync(string gameId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Helix-клиент с проверкой токена, обновлением по 401 и одним повтором
/// при временной сетевой ошибке. Токены никогда не попадают в сообщения об ошибках.
/// </summary>
public sealed class TwitchApiClient : ITwitchApiClient
{
    private const string HelixBase = "https://api.twitch.tv/helix";
    private static readonly TimeSpan ValidationLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly Func<string?> clientIdProvider;
    private readonly TwitchTokenStore tokenStore;
    private readonly TwitchOAuthClient oauth;
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate = new(1, 1);

    private TwitchValidation? validation;
    private string? validatedAccessToken;
    private DateTimeOffset validatedAt = DateTimeOffset.MinValue;

    public TwitchApiClient(
        Func<string?> clientIdProvider,
        TwitchTokenStore tokenStore,
        TwitchOAuthClient oauth,
        HttpClient http)
    {
        this.clientIdProvider = clientIdProvider;
        this.tokenStore = tokenStore;
        this.oauth = oauth;
        this.http = http;
    }

    public async Task<TwitchUser> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var body = await SendAuthorizedAsync(
                HttpMethod.Get,
                $"{HelixBase}/users",
                body: null,
                cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0)
        {
            throw new TwitchApiException("Twitch не вернул данные пользователя.");
        }

        var user = data[0];
        return new TwitchUser(
            ReadString(user, "id"),
            ReadString(user, "login"),
            ReadString(user, "display_name"));
    }

    public async Task<TwitchCategory?> FindFirstCategoryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }

        var url = $"{HelixBase}/search/categories?query={Uri.EscapeDataString(trimmed)}&first=20";
        var body = await SendAuthorizedAsync(HttpMethod.Get, url, body: null, cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0)
        {
            return null;
        }

        var category = data[0];
        return new TwitchCategory(
            ReadString(category, "id"),
            ReadString(category, "name"),
            category.TryGetProperty("box_art_url", out var boxArt) &&
            boxArt.ValueKind == JsonValueKind.String
                ? boxArt.GetString()
                : null);
    }

    public async Task UpdateTitleAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        var url = $"{HelixBase}/channels?broadcaster_id={Uri.EscapeDataString(auth.UserId)}";
        await SendAuthorizedAsync(auth, HttpMethod.Patch, url, new { title }, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateCategoryAsync(
        string gameId,
        CancellationToken cancellationToken = default)
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        var url = $"{HelixBase}/channels?broadcaster_id={Uri.EscapeDataString(auth.UserId)}";
        await SendAuthorizedAsync(auth, HttpMethod.Patch, url, new { game_id = gameId }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> SendAuthorizedAsync(
        HttpMethod method,
        string url,
        object? body,
        CancellationToken cancellationToken)
    {
        var auth = await EnsureAuthorizedAsync(cancellationToken).ConfigureAwait(false);
        return await SendAuthorizedAsync(auth, method, url, body, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> SendAuthorizedAsync(
        Authorized auth,
        HttpMethod method,
        string url,
        object? body,
        CancellationToken cancellationToken)
    {
        var (status, responseBody) = await SendOnceAsync(auth, method, url, body, cancellationToken)
            .ConfigureAwait(false);

        if (status == HttpStatusCode.Unauthorized)
        {
            // Токен мог быть отозван между проверкой и запросом. Обновляем один раз.
            auth = await RefreshAndAuthorizeAsync(cancellationToken).ConfigureAwait(false);
            (status, responseBody) = await SendOnceAsync(auth, method, url, body, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!IsSuccess(status))
        {
            throw new TwitchApiException(DescribeError(status, responseBody));
        }

        return responseBody;
    }

    private async Task<(HttpStatusCode Status, string Body)> SendOnceAsync(
        Authorized auth,
        HttpMethod method,
        string url,
        object? body,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            request.Headers.TryAddWithoutValidation("Client-Id", auth.ClientId);

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            try
            {
                using var response = await http
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);
                var responseBody = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                // PATCH установки названия/категории идемпотентен, поэтому один
                // повтор при 429/5xx безопасен. Дольше не повторяем, чтобы команда
                // не зависала и не выполнялась повторно спустя время.
                if (attempt == 0 && ShouldRetry(response.StatusCode))
                {
                    await Task.Delay(RetryDelay(response), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return (response.StatusCode, responseBody);
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<Authorized> EnsureAuthorizedAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var clientId = RequireClientId();
            var tokens = tokenStore.Load() ??
                         throw new TwitchNotConnectedException(
                             "Twitch не подключён. Откройте настройки и подключите Twitch.");

            if (tokens.ExpiresAtUtc <= DateTimeOffset.UtcNow.Add(RefreshMargin))
            {
                tokens = await RefreshTokensAsync(clientId, tokens, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (validation is null ||
                validatedAccessToken != tokens.AccessToken ||
                validatedAt <= DateTimeOffset.UtcNow.Subtract(ValidationLifetime))
            {
                try
                {
                    validation = await oauth.ValidateAsync(tokens.AccessToken, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TwitchAuthorizationException)
                {
                    tokens = await RefreshTokensAsync(clientId, tokens, cancellationToken)
                        .ConfigureAwait(false);
                    validation = await oauth.ValidateAsync(tokens.AccessToken, cancellationToken)
                        .ConfigureAwait(false);
                }

                validatedAccessToken = tokens.AccessToken;
                validatedAt = DateTimeOffset.UtcNow;
                EnsureClientAndScope(clientId, validation);
            }

            return new Authorized(
                tokens.AccessToken,
                clientId,
                validation.UserId,
                validation.Login);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Authorized> RefreshAndAuthorizeAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var clientId = RequireClientId();
            var previous = validation ??
                           throw new TwitchAuthorizationException(
                               "Сессия Twitch истекла. Подключите Twitch заново.");
            var tokens = tokenStore.Load() ??
                         throw new TwitchNotConnectedException(
                             "Twitch не подключён. Откройте настройки и подключите Twitch.");

            tokens = await RefreshTokensAsync(clientId, tokens, cancellationToken)
                .ConfigureAwait(false);
            return new Authorized(tokens.AccessToken, clientId, previous.UserId, previous.Login);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TwitchTokens> RefreshTokensAsync(
        string clientId,
        TwitchTokens tokens,
        CancellationToken cancellationToken)
    {
        try
        {
            var refreshed = await oauth
                .RefreshAsync(clientId, tokens.RefreshToken, cancellationToken)
                .ConfigureAwait(false);
            tokenStore.Save(refreshed);
            validation = null;
            validatedAccessToken = null;
            validatedAt = DateTimeOffset.MinValue;
            return refreshed;
        }
        catch (TwitchAuthorizationException)
        {
            tokenStore.Delete();
            validation = null;
            validatedAccessToken = null;
            validatedAt = DateTimeOffset.MinValue;
            throw;
        }
    }

    private void EnsureClientAndScope(string clientId, TwitchValidation result)
    {
        if (!string.Equals(result.ClientId, clientId, StringComparison.Ordinal))
        {
            tokenStore.Delete();
            throw new TwitchAuthorizationException(
                "Токен Twitch относится к другому Client ID. Подключите Twitch заново.");
        }

        if (!result.Scopes.Contains(TwitchOAuthClient.RequiredScope, StringComparer.Ordinal))
        {
            tokenStore.Delete();
            throw new TwitchAuthorizationException(
                "Twitch не выдал право channel:manage:broadcast. Подключите Twitch заново.");
        }
    }

    private string RequireClientId()
    {
        var clientId = clientIdProvider()?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new TwitchNotConnectedException(
                "Twitch не настроен: укажите Client ID в настройках.");
        }

        return clientId;
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and <= 299;

    private static bool ShouldRetry(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta <= TimeSpan.FromSeconds(5) ? delta : TimeSpan.FromSeconds(5);
        }

        return TimeSpan.FromSeconds(1);
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string DescribeError(HttpStatusCode status, string body)
    {
        var message = ReadErrorMessage(body);
        return message.Length == 0
            ? $"Twitch вернул ошибку {(int)status}."
            : message.Length <= 200
                ? $"Twitch: {message}"
                : $"Twitch: {message[..197]}...";
    }

    private static string ReadErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("message", out var message) &&
                   message.ValueKind == JsonValueKind.String
                ? message.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private sealed record Authorized(
        string AccessToken,
        string ClientId,
        string UserId,
        string Login);
}
