using ObsVoiceAssistant.Integrations.Twitch;

namespace ObsVoiceAssistant.UnitTests;

public sealed class TwitchTitleComposerTests
{
    [Fact]
    public void Compose_AppendsMissingTagsInOldOrder()
    {
        var result = TwitchTitleComposer.Compose(
            "Вечерний ranked",
            "Старое название !tg !exitlag");

        Assert.True(result.IsSuccess);
        Assert.Equal("Вечерний ranked !tg !exitlag", result.Title);
    }

    [Fact]
    public void Compose_DoesNotDuplicateTagsIgnoringCase()
    {
        var result = TwitchTitleComposer.Compose(
            "Вечерний !TG",
            "Старое !tg !exitlag");

        Assert.True(result.IsSuccess);
        Assert.Equal("Вечерний !TG !exitlag", result.Title);
    }

    [Fact]
    public void Compose_KeepsTagPositionsAlreadyInNewTitle()
    {
        var result = TwitchTitleComposer.Compose(
            "!tg Вечерний ranked",
            "Старое !exitlag");

        Assert.True(result.IsSuccess);
        Assert.Equal("!tg Вечерний ranked !exitlag", result.Title);
    }

    [Fact]
    public void Compose_WithoutOldTags_DoesNotChangeTitle()
    {
        var result = TwitchTitleComposer.Compose(
            "Вечерний ranked",
            "Старое название без тегов");

        Assert.True(result.IsSuccess);
        Assert.Equal("Вечерний ranked", result.Title);
    }

    [Fact]
    public void Compose_TrimsTrailingPunctuation()
    {
        var result = TwitchTitleComposer.Compose(
            "Вечерний ranked.",
            "Старое !tg,");

        Assert.True(result.IsSuccess);
        Assert.Equal("Вечерний ranked !tg", result.Title);
    }

    [Fact]
    public void Compose_DeduplicatesTagsFromOldTitle()
    {
        var result = TwitchTitleComposer.Compose(
            "Вечерний",
            "Старое !tg !TG !exitlag");

        Assert.True(result.IsSuccess);
        Assert.Equal("Вечерний !tg !exitlag", result.Title);
    }

    [Fact]
    public void Compose_IgnoresWordsWithoutExclamationMark()
    {
        var result = TwitchTitleComposer.Compose("Вечерний", "Старое tg exitlag");

        Assert.True(result.IsSuccess);
        Assert.Equal("Вечерний", result.Title);
    }

    [Fact]
    public void Compose_RejectsEmptyTitle()
    {
        var result = TwitchTitleComposer.Compose("   ", "Старое !tg");

        Assert.False(result.IsSuccess);
        Assert.Contains("Не указано", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_RejectsResultLongerThanTwitchLimit()
    {
        var title = new string('а', 139);
        var result = TwitchTitleComposer.Compose(title, "Старое !tg");

        Assert.False(result.IsSuccess);
        Assert.Contains("140", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_AllowsExactlyTwitchLimit()
    {
        var title = new string('а', 136);
        var result = TwitchTitleComposer.Compose(title, "Старое !tg");

        Assert.True(result.IsSuccess);
        Assert.Equal(140, result.Title.Length);
    }

    [Theory]
    [InlineData("!tg", new[] { "!tg" })]
    [InlineData("!tg, !exitlag.", new[] { "!tg", "!exitlag" })]
    [InlineData("текст !tg текст", new[] { "!tg" })]
    [InlineData("! !!! !!tg", new[] { "!!tg" })]
    public void ExtractTags_ReturnsTagWords(string title, string[] expected)
    {
        Assert.Equal(expected, TwitchTitleComposer.ExtractTags(title));
    }
}
