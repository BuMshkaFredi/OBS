using System.Net;
using ObsVoiceAssistant.Configuration;
using ObsVoiceAssistant.Integrations.Twitch;

namespace ObsVoiceAssistant.UnitTests;

public sealed class TwitchServiceTests
{
    [Fact]
    public async Task CompleteConnect_SavesTokensAndReturnsTwitchUser()
    {
        var directory = CreateTempDirectory();
        try
        {
            var settings = new SettingsService(directory);
            settings.Save(settings.Load() with { TwitchClientId = "client-id" });
            var secrets = new InMemorySecretStore();
            var handler = new StubHttpMessageHandler();
            handler.EnqueueJson(
                HttpStatusCode.OK,
                "{\"device_code\":\"device-1\",\"user_code\":\"ABCD1234\"," +
                "\"verification_uri\":\"https://www.twitch.tv/activate\",\"expires_in\":900,\"interval\":1}");
            handler.EnqueueJson(
                HttpStatusCode.OK,
                "{\"access_token\":\"access-1\",\"refresh_token\":\"refresh-1\"," +
                "\"expires_in\":14400,\"scope\":[\"channel:manage:broadcast\"]}");
            handler.EnqueueJson(
                HttpStatusCode.OK,
                "{\"client_id\":\"client-id\",\"login\":\"streamer\",\"user_id\":\"user-1\"," +
                "\"scopes\":[\"channel:manage:broadcast\"],\"expires_in\":14000}");
            handler.EnqueueJson(
                HttpStatusCode.OK,
                "{\"data\":[{\"id\":\"user-1\",\"login\":\"streamer\",\"display_name\":\"Streamer\"}]}");
            handler.EnqueueEmpty(HttpStatusCode.NoContent);

            using var service = new TwitchService(settings, secrets, handler);
            var deviceCode = await service.BeginConnectAsync();
            var user = await service.CompleteConnectAsync(deviceCode);

            Assert.Equal("user-1", user.Id);
            Assert.Equal("Streamer", user.DisplayName);
            Assert.True(service.HasSavedTokens);
            Assert.Equal("access-1", new TwitchTokenStore(secrets).Load()?.AccessToken);

            await service.DisconnectAsync();

            Assert.False(service.HasSavedTokens);
            Assert.EndsWith("/oauth2/revoke", handler.Requests[^1].Uri?.AbsolutePath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BeginConnect_WithoutClientId_ThrowsFriendlyError()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new TwitchService(
                new SettingsService(directory),
                new InMemorySecretStore(),
                new StubHttpMessageHandler());

            var exception = await Assert.ThrowsAsync<TwitchNotConnectedException>(
                () => service.BeginConnectAsync());

            Assert.Contains("Client ID", exception.Message, StringComparison.Ordinal);
            service.Dispose();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ObsVoiceAssistantTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
