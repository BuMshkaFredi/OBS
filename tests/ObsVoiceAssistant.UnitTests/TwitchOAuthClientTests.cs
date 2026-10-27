using System.Net;
using System.Net.Http;
using ObsVoiceAssistant.Integrations.Twitch;

namespace ObsVoiceAssistant.UnitTests;

public sealed class TwitchOAuthClientTests
{
    private const string DeviceJson =
        "{\"device_code\":\"device-1\",\"user_code\":\"ABCD1234\"," +
        "\"verification_uri\":\"https://www.twitch.tv/activate\",\"expires_in\":900,\"interval\":1}";

    [Fact]
    public async Task StartDeviceFlow_ParsesCodeAndRequestsOnlyBroadcastScope()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, DeviceJson);
        using var http = new HttpClient(handler);
        var oauth = new TwitchOAuthClient(http);

        var code = await oauth.StartDeviceFlowAsync("client-id");

        Assert.Equal("device-1", code.DeviceCode);
        Assert.Equal("ABCD1234", code.UserCode);
        Assert.Equal("https://www.twitch.tv/activate", code.VerificationUri);
        Assert.Equal(900, code.ExpiresInSeconds);
        Assert.Equal(1, code.IntervalSeconds);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://id.twitch.tv/oauth2/device", request.Uri?.ToString());
        var decoded = Uri.UnescapeDataString(request.Body ?? string.Empty);
        Assert.Contains("client_id=client-id", decoded, StringComparison.Ordinal);
        Assert.Contains("scopes=channel:manage:broadcast", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollForToken_RetriesWhileAuthorizationIsPending()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.BadRequest, "{\"message\":\"authorization_pending\"}");
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"access_token\":\"access-1\",\"refresh_token\":\"refresh-1\"," +
            "\"expires_in\":14400,\"scope\":[\"channel:manage:broadcast\"]}");
        using var http = new HttpClient(handler);
        var oauth = new TwitchOAuthClient(http);
        var code = new TwitchDeviceCode("device-1", "ABCD1234", "https://www.twitch.tv/activate", 60, 1);

        var tokens = await oauth.PollForTokenAsync("client-id", code);

        Assert.Equal("access-1", tokens.AccessToken);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        Assert.Contains(TwitchOAuthClient.RequiredScope, tokens.Scopes);
        Assert.True(tokens.ExpiresAtUtc > DateTimeOffset.UtcNow.AddHours(3));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("device_code=device-1", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("grant_type=urn", handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollForToken_ThrowsWhenUserDeniesAccess()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.BadRequest, "{\"message\":\"access_denied\"}");
        using var http = new HttpClient(handler);
        var oauth = new TwitchOAuthClient(http);
        var code = new TwitchDeviceCode("device-1", "ABCD1234", "https://www.twitch.tv/activate", 60, 1);

        var exception = await Assert.ThrowsAsync<TwitchAuthorizationException>(
            () => oauth.PollForTokenAsync("client-id", code));

        Assert.Contains("отклон", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_KeepsOldRefreshTokenWhenTwitchDoesNotReturnNewOne()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"access_token\":\"access-2\",\"expires_in\":14400," +
            "\"scope\":[\"channel:manage:broadcast\"]}");
        using var http = new HttpClient(handler);
        var oauth = new TwitchOAuthClient(http);

        var tokens = await oauth.RefreshAsync("client-id", "refresh-1");

        Assert.Equal("access-2", tokens.AccessToken);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        Assert.Contains("grant_type=refresh_token", handler.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_ThrowsReconnectMessageOnInvalidGrant()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.BadRequest, "{\"message\":\"Invalid refresh token\"}");
        using var http = new HttpClient(handler);
        var oauth = new TwitchOAuthClient(http);

        var exception = await Assert.ThrowsAsync<TwitchAuthorizationException>(
            () => oauth.RefreshAsync("client-id", "refresh-1"));

        Assert.Contains("Подключите Twitch заново", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_UsesOAuthHeaderAndParsesUser()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"client_id\":\"client-id\",\"login\":\"streamer\",\"user_id\":\"user-1\"," +
            "\"scopes\":[\"channel:manage:broadcast\"],\"expires_in\":14000}");
        using var http = new HttpClient(handler);
        var oauth = new TwitchOAuthClient(http);

        var validation = await oauth.ValidateAsync("access-1");

        Assert.Equal("client-id", validation.ClientId);
        Assert.Equal("user-1", validation.UserId);
        Assert.Equal("streamer", validation.Login);
        Assert.Contains(TwitchOAuthClient.RequiredScope, validation.Scopes);
        Assert.Equal("OAuth access-1", handler.Requests[0].Authorization);
    }
}
