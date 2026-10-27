using ObsVoiceAssistant.Integrations.Twitch;

namespace ObsVoiceAssistant.UnitTests;

public sealed class TwitchTokenStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsTokens()
    {
        var store = new TwitchTokenStore(new InMemorySecretStore());
        var expiresAt = DateTimeOffset.UtcNow.AddHours(4);
        var tokens = new TwitchTokens(
            "access-1",
            "refresh-1",
            expiresAt,
            new[] { TwitchOAuthClient.RequiredScope });

        store.Save(tokens);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("access-1", loaded.AccessToken);
        Assert.Equal("refresh-1", loaded.RefreshToken);
        Assert.Equal(expiresAt, loaded.ExpiresAtUtc);
        Assert.Contains(TwitchOAuthClient.RequiredScope, loaded.Scopes);
        Assert.True(store.Exists());
    }

    [Fact]
    public void Load_WithCorruptedJson_ReturnsNull()
    {
        var secrets = new InMemorySecretStore();
        secrets.Save("twitch.tokens", "not-json");
        var store = new TwitchTokenStore(secrets);

        Assert.Null(store.Load());
        Assert.False(store.Exists());
    }

    [Fact]
    public void Delete_RemovesTokens()
    {
        var store = new TwitchTokenStore(new InMemorySecretStore());
        store.Save(new TwitchTokens(
            "access-1",
            "refresh-1",
            DateTimeOffset.UtcNow.AddHours(1),
            Array.Empty<string>()));

        store.Delete();

        Assert.Null(store.Load());
        Assert.False(store.Exists());
    }
}
