using ObsVoiceAssistant.Integrations.Media;

namespace ObsVoiceAssistant.UnitTests;

public sealed class MediaSessionSelectorTests
{
    [Fact]
    public void SelectIndex_PrefersPlayingYandexSession()
    {
        var sessions = new[]
        {
            new MediaSessionInfo("Spotify.exe", true),
            new MediaSessionInfo("A0252B67.YandexMusic_8wekyb3d8bbwe!App", false),
            new MediaSessionInfo("ru.yandex.music", true)
        };

        var index = MediaSessionSelector.SelectIndex(sessions);

        Assert.Equal(2, index);
    }

    [Fact]
    public void SelectIndex_FallsBackToPausedYandexSession()
    {
        var sessions = new[]
        {
            new MediaSessionInfo("chrome.exe", true),
            new MediaSessionInfo("YandexMusic.exe", false)
        };

        var index = MediaSessionSelector.SelectIndex(sessions);

        Assert.Equal(1, index);
    }

    [Fact]
    public void SelectIndex_ReturnsMinusOneWhenYandexIsMissing()
    {
        var sessions = new[]
        {
            new MediaSessionInfo("Spotify.exe", true),
            new MediaSessionInfo("chrome.exe", true)
        };

        Assert.Equal(-1, MediaSessionSelector.SelectIndex(sessions));
    }

    [Theory]
    [InlineData("A0252B67.YandexMusic_8wekyb3d8bbwe!App")]
    [InlineData("YandexMusic.exe")]
    [InlineData("ru.yandex.music")]
    [InlineData("Яндекс.Музыка")]
    public void IsYandex_MatchesApplicationIds(string appId)
    {
        Assert.True(MediaSessionSelector.IsYandex(appId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("chrome.exe")]
    [InlineData("Spotify.exe")]
    public void IsYandex_RejectsOtherApplicationIds(string? appId)
    {
        Assert.False(MediaSessionSelector.IsYandex(appId));
    }
}
