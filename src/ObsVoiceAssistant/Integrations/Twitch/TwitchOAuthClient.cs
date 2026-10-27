using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>
/// OAuth Device Code Grant для публичного desktop-клиента. Client secret не нужен.
/// Документация: https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/#device-code-grant-flow
/// </summary>
public sealed class TwitchOAuthClient
{
    public const string RequiredScope = "channel:manage:broadcast";

    private const string DeviceEndpoint = "https://id.twitch.tv/oauth2/device";
    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string ValidateEndpoint = "https://id.twitch.tv/oauth2/validate";
    private const string RevokeEndpoint = "https://id.twitch.tv/oauth2/revoke";

    private readonly HttpClient http;

    public TwitchOAuthClient(HttpClient http)
    {
        this.http = http;
    }

    public async Task<TwitchDeviceCode> StartDeviceFlowAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scopes"] = RequiredScope
        });
        using var response = await http.PostAsync(DeviceEndpoint, content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwitchApiException(DescribeError(response.StatusCode, body));
        }

        using var document = ParseJson(body);
        var root = document.RootElement;

        return new TwitchDeviceCode(
            ReadString(root, "device_code"),
            ReadString(root, "user_code"),
            ReadString(root, "verification_uri"),
            ReadInt(root, "expires_in", 1800),
            Math.Max(1, ReadInt(root, "interval", 5)));
    }

    /// <summary>
    /// Опрашивает Twitch, пока пользователь не подтвердит доступ. Возвращает токены
    /// только после успешного ответа; истёкший или отменённый код — ошибка.
    /// </summary>
    public async Task<TwitchTokens> PollForTokenAsync(
        string clientId,
        TwitchDeviceCode deviceCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(deviceCode);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, deviceCode.ExpiresInSeconds));
        var interval = Math.Max(1, deviceCode.IntervalSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scopes"] = RequiredScope,
                ["device_code"] = deviceCode.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });
            using var response = await http.PostAsync(TokenEndpoint, content, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return ParseTokens(body);
            }

            var message = ReadErrorMessage(body);
            switch (message)
            {
                case "authorization_pending":
                    break;

                case "slow_down":
                    interval += 5;
                    break;

                case "access_denied":
                    throw new TwitchAuthorizationException("Доступ к Twitch отклонён.");

                case "expired_token":
                case "invalid device code":
                    throw new TwitchAuthorizationException(
                        "Код подключения Twitch истёк. Начните подключение заново.");

                default:
                    throw new TwitchApiException(DescribeError(response.StatusCode, body));
            }

            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);
        }

        throw new TwitchAuthorizationException(
            "Twitch не подтвердил доступ за отведённое время. Начните подключение заново.");
    }

    public async Task<TwitchTokens> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });
        using var response = await http.PostAsync(TokenEndpoint, content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new TwitchAuthorizationException(
                "Сессия Twitch истекла. Подключите Twitch заново в настройках.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new TwitchApiException(DescribeError(response.StatusCode, body));
        }

        // Twitch может не вернуть новый refresh token; тогда старый остаётся рабочим.
        return ParseTokens(body, refreshToken);
    }

    public async Task<TwitchValidation> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, ValidateEndpoint);
        request.Headers.TryAddWithoutValidation("Authorization", $"OAuth {accessToken}");

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new TwitchAuthorizationException("Токен Twitch больше не действителен.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new TwitchApiException(DescribeError(response.StatusCode, body));
        }

        using var document = ParseJson(body);
        var root = document.RootElement;

        return new TwitchValidation(
            ReadString(root, "client_id"),
            ReadString(root, "user_id"),
            ReadString(root, "login"),
            ReadScopes(root, "scopes"),
            ReadInt(root, "expires_in", 0));
    }

    /// <summary>Отзывает токен на стороне Twitch; локальное удаление делает вызывающий код.</summary>
    public async Task RevokeAsync(
        string clientId,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["token"] = accessToken
        });
        using var response = await http.PostAsync(RevokeEndpoint, content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwitchApiException(DescribeError(response.StatusCode, body));
        }
    }

    private static TwitchTokens ParseTokens(string body, string? fallbackRefreshToken = null)
    {
        using var document = ParseJson(body);
        var root = document.RootElement;

        var accessToken = ReadString(root, "access_token");
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) &&
                           refresh.ValueKind == JsonValueKind.String
            ? refresh.GetString() ?? fallbackRefreshToken ?? string.Empty
            : fallbackRefreshToken ?? string.Empty;

        return new TwitchTokens(
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, ReadInt(root, "expires_in", 14400))),
            ReadScopes(root, "scope"));
    }

    private static IReadOnlyList<string> ReadScopes(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var scopes) ||
            scopes.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return scopes
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(scope => scope.Length > 0)
            .ToArray();
    }

    private static string ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement root, string property, int fallback) =>
        root.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;

    private static JsonDocument ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new TwitchApiException("Twitch вернул неожиданный ответ.");
        }
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

    private static string DescribeError(HttpStatusCode status, string body)
    {
        var message = ReadErrorMessage(body);
        if (message.Length == 0)
        {
            return $"Twitch вернул ошибку {(int)status}.";
        }

        // Текст ошибки Twitch не содержит токенов, но всё равно ограничиваем длину.
        return message.Length <= 200 ? $"Twitch: {message}" : $"Twitch: {message[..197]}...";
    }
}
