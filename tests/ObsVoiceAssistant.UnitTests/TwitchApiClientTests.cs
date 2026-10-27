using System.Net;
using System.Net.Http;
using ObsVoiceAssistant.Integrations.Twitch;

namespace ObsVoiceAssistant.UnitTests;

public sealed class TwitchApiClientTests
{
    private const string ClientId = "client-id";
    private const string ValidationJson =
        "{\"client_id\":\"client-id\",\"login\":\"streamer\",\"user_id\":\"user-1\"," +
        "\"scopes\":[\"channel:manage:broadcast\"],\"expires_in\":14000}";

    [Fact]
    public async Task FindFirstCategory_ReturnsFirstSearchResult()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ValidationJson);
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"data\":[" +
            "{\"id\":\"cat-1\",\"name\":\"First\",\"box_art_url\":\"https://example/1.jpg\"}," +
            "{\"id\":\"cat-2\",\"name\":\"Second\"}]}");
        using var http = new HttpClient(handler);
        var store = CreateTokenStore();
        var api = CreateApi(http, store);

        var category = await api.FindFirstCategoryAsync("first");

        Assert.NotNull(category);
        Assert.Equal("cat-1", category.Id);
        Assert.Equal("First", category.Name);
        Assert.Equal("https://example/1.jpg", category.BoxArtUrl);
        Assert.Equal("OAuth access-1", handler.Requests[0].Authorization);
        Assert.Contains("query=first", handler.Requests[1].Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("first=20", handler.Requests[1].Uri?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindFirstCategory_ReturnsNullWhenNothingFound()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ValidationJson);
        handler.EnqueueJson(HttpStatusCode.OK, "{\"data\":[]}");
        using var http = new HttpClient(handler);
        var api = CreateApi(http, CreateTokenStore());

        var category = await api.FindFirstCategoryAsync("unknown");

        Assert.Null(category);
    }

    [Fact]
    public async Task GetChannel_ReturnsTitleAndGame()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ValidationJson);
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"data\":[{\"title\":\"Вечерний !tg\",\"game_name\":\"Dota 2\",\"game_id\":\"game-1\"}]}");
        using var http = new HttpClient(handler);
        var api = CreateApi(http, CreateTokenStore());

        var channel = await api.GetChannelAsync();

        Assert.Equal("Вечерний !tg", channel.Title);
        Assert.Equal("Dota 2", channel.GameName);
        Assert.Equal("game-1", channel.GameId);
        Assert.Contains("broadcaster_id=user-1", handler.Requests[1].Uri?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStreamStatus_ReturnsLiveViewerCount()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ValidationJson);
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"data\":[{\"type\":\"live\",\"viewer_count\":1234}]}");
        using var http = new HttpClient(handler);
        var api = CreateApi(http, CreateTokenStore());

        var status = await api.GetStreamStatusAsync();

        Assert.True(status.IsLive);
        Assert.Equal(1234, status.Viewers);
        Assert.Contains("user_id=user-1", handler.Requests[1].Uri?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStreamStatus_EmptyDataMeansOffline()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ValidationJson);
        handler.EnqueueJson(HttpStatusCode.OK, "{\"data\":[]}");
        using var http = new HttpClient(handler);
        var api = CreateApi(http, CreateTokenStore());

        var status = await api.GetStreamStatusAsync();

        Assert.False(status.IsLive);
        Assert.Equal(0, status.Viewers);
    }

    [Fact]
    public async Task UpdateTitle_SendsPatchWithBroadcasterIdAndTitle()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ValidationJson);
        handler.EnqueueEmpty(HttpStatusCode.NoContent);
        using var http = new HttpClient(handler);
        var api = CreateApi(http, CreateTokenStore());

        await api.UpdateTitleAsync("Новое название");

        var request = handler.Requests[1];
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Contains("broadcaster_id=user-1", request.Uri?.Query, StringComparison.Ordinal);
        Assert.Contains("\\u041D\\u043E\\u0432\\u043E\\u0435 ", request.Body, StringComparison.Ordinal);
        Assert.Equal("Bearer access-1", request.Authorization);
        Assert.Equal(ClientId, request.ClientId);
    }

    [Fact]
    public async Task UpdateCategory_SendsGameId()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ValidationJson);
        handler.EnqueueEmpty(HttpStatusCode.NoContent);
        using var http = new HttpClient(handler);
        var api = CreateApi(http, CreateTokenStore());

        await api.UpdateCategoryAsync("game-42");

        var request = handler.Requests[1];
        Assert.Equal(HttpMethod.Patch, request.Method);
        Assert.Contains("\"game_id\":\"game-42\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnauthorizedResponse_RefreshesTokenAndRetriesOnce()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(HttpStatusCode.OK, ValidationJson);
        handler.EnqueueJson(HttpStatusCode.Unauthorized, "{\"message\":\"Invalid OAuth token\"}");
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\"," +
            "\"expires_in\":14400,\"scope\":[\"channel:manage:broadcast\"]}");
        handler.EnqueueEmpty(HttpStatusCode.NoContent);
        using var http = new HttpClient(handler);
        var store = CreateTokenStore();
        var api = CreateApi(http, store);

        await api.UpdateTitleAsync("After refresh");

        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains("grant_type=refresh_token", handler.Requests[2].Body, StringComparison.Ordinal);
        Assert.Equal("Bearer access-2", handler.Requests[3].Authorization);
        Assert.Equal("access-2", store.Load()?.AccessToken);
        Assert.Equal("refresh-2", store.Load()?.RefreshToken);
    }

    [Fact]
    public async Task MissingScope_ThrowsAndDeletesSavedToken()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"client_id\":\"client-id\",\"login\":\"streamer\",\"user_id\":\"user-1\"," +
            "\"scopes\":[\"chat:read\"],\"expires_in\":14000}");
        using var http = new HttpClient(handler);
        var store = CreateTokenStore();
        var api = CreateApi(http, store);

        var exception = await Assert.ThrowsAsync<TwitchAuthorizationException>(
            () => api.FindFirstCategoryAsync("first"));

        Assert.Contains("channel:manage:broadcast", exception.Message, StringComparison.Ordinal);
        Assert.Null(store.Load());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NotConnected_ThrowsWithoutHttpRequest()
    {
        var handler = new StubHttpMessageHandler();
        using var http = new HttpClient(handler);
        var store = new TwitchTokenStore(new InMemorySecretStore());
        var api = CreateApi(http, store);

        await Assert.ThrowsAsync<TwitchNotConnectedException>(
            () => api.FindFirstCategoryAsync("first"));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExpiredToken_IsRefreshedBeforeValidation()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueJson(
            HttpStatusCode.OK,
            "{\"access_token\":\"access-2\",\"refresh_token\":\"refresh-2\"," +
            "\"expires_in\":14400,\"scope\":[\"channel:manage:broadcast\"]}");
        handler.EnqueueJson(HttpStatusCode.OK, ValidationJson);
        handler.EnqueueJson(HttpStatusCode.OK, "{\"data\":[]}");
        using var http = new HttpClient(handler);
        var store = CreateTokenStore(DateTimeOffset.UtcNow.AddMinutes(1));
        var api = CreateApi(http, store);

        await api.FindFirstCategoryAsync("first");

        Assert.Contains("refresh_token", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.EndsWith("/oauth2/validate", handler.Requests[1].Uri?.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("access-2", store.Load()?.AccessToken);
    }

    private static TwitchApiClient CreateApi(HttpClient http, TwitchTokenStore store) =>
        new(() => ClientId, store, new TwitchOAuthClient(http), http);

    private static TwitchTokenStore CreateTokenStore(DateTimeOffset? expiresAt = null)
    {
        var store = new TwitchTokenStore(new InMemorySecretStore());
        store.Save(new TwitchTokens(
            "access-1",
            "refresh-1",
            expiresAt ?? DateTimeOffset.UtcNow.AddHours(4),
            new[] { TwitchOAuthClient.RequiredScope }));
        return store;
    }
}
