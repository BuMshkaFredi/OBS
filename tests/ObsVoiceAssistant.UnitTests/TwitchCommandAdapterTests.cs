using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Integrations.Twitch;

namespace ObsVoiceAssistant.UnitTests;

public sealed class TwitchCommandAdapterTests
{
    [Fact]
    public async Task PrepareCategory_ReplacesCommandWithFirstTwitchResult()
    {
        var api = new FakeTwitchApi
        {
            Category = new TwitchCategory("game-1", "Counter-Strike 2", null)
        };
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(CommandIntent.SetStreamCategory, "кс 2");

        var prepared = await adapter.PrepareAsync(command);

        Assert.Equal("Counter-Strike 2", prepared.Argument);
        Assert.Equal("game-1", prepared.ArgumentId);
        Assert.Equal("кс 2", api.LastCategoryQuery);
    }

    [Fact]
    public async Task PrepareCategory_ThrowsWhenNothingFound()
    {
        var adapter = new TwitchCommandAdapter(new FakeTwitchApi());
        var command = new ParsedCommand(CommandIntent.SetStreamCategory, "несуществующее");

        var exception = await Assert.ThrowsAsync<TwitchApiException>(
            () => adapter.PrepareAsync(command));

        Assert.Contains("не найдена", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareOtherCommand_DoesNotCallTwitch()
    {
        var api = new FakeTwitchApi();
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(CommandIntent.StartRecording);

        var prepared = await adapter.PrepareAsync(command);

        Assert.Same(command, prepared);
        Assert.Null(api.LastCategoryQuery);
    }

    [Fact]
    public async Task PrepareTitle_AppendsCurrentTags()
    {
        var api = new FakeTwitchApi
        {
            Channel = new TwitchChannel("Старое !tg !exitlag", string.Empty, string.Empty)
        };
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(CommandIntent.SetStreamTitle, "Вечерний ranked");

        var prepared = await adapter.PrepareAsync(command);

        Assert.Equal("Вечерний ranked !tg !exitlag", prepared.Argument);
    }

    [Fact]
    public async Task PrepareTitle_DoesNotDuplicateTagsCaseInsensitively()
    {
        var api = new FakeTwitchApi
        {
            Channel = new TwitchChannel("Старое !TG !exitlag", string.Empty, string.Empty)
        };
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(CommandIntent.SetStreamTitle, "Вечерний !tg");

        var prepared = await adapter.PrepareAsync(command);

        Assert.Equal("Вечерний !tg !exitlag", prepared.Argument);
    }

    [Fact]
    public async Task PrepareTitle_RejectsTooLongResultWithoutUpdate()
    {
        var api = new FakeTwitchApi
        {
            Channel = new TwitchChannel("!tag", string.Empty, string.Empty)
        };
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(CommandIntent.SetStreamTitle, new string('а', 140));

        var exception = await Assert.ThrowsAsync<TwitchApiException>(
            () => adapter.PrepareAsync(command));

        Assert.Contains("140", exception.Message, StringComparison.Ordinal);
        Assert.Null(api.LastTitle);
    }

    [Fact]
    public async Task PrepareTitle_ReadFailureRejectsCommand()
    {
        var api = new FakeTwitchApi { ChannelFailure = new TwitchApiException("Twitch недоступен") };
        var adapter = new TwitchCommandAdapter(api);

        await Assert.ThrowsAsync<TwitchApiException>(
            () => adapter.PrepareAsync(
                new ParsedCommand(CommandIntent.SetStreamTitle, "Название")));
    }

    [Fact]
    public async Task ExecuteTitle_UpdatesTwitchAndReturnsResult()
    {
        var api = new FakeTwitchApi();
        var adapter = new TwitchCommandAdapter(api);

        var result = await adapter.ExecuteAsync(
            new ParsedCommand(CommandIntent.SetStreamTitle, "  Новый стрим  "));

        Assert.Equal("Новый стрим", api.LastTitle);
        Assert.Contains("Новый стрим", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteTitle_RejectsEmptyAndTooLongTitle()
    {
        var api = new FakeTwitchApi();
        var adapter = new TwitchCommandAdapter(api);

        await Assert.ThrowsAsync<TwitchApiException>(
            () => adapter.ExecuteAsync(new ParsedCommand(CommandIntent.SetStreamTitle, " ")));
        await Assert.ThrowsAsync<TwitchApiException>(
            () => adapter.ExecuteAsync(
                new ParsedCommand(CommandIntent.SetStreamTitle, new string('а', 141))));

        Assert.Null(api.LastTitle);
    }

    [Fact]
    public async Task ExecuteCategory_UsesResolvedGameId()
    {
        var api = new FakeTwitchApi();
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(
            CommandIntent.SetStreamCategory,
            "Counter-Strike 2",
            ArgumentId: "game-1");

        var result = await adapter.ExecuteAsync(command);

        Assert.Equal("game-1", api.LastGameId);
        Assert.Contains("Counter-Strike 2", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteCategory_WithoutPreparedId_Throws()
    {
        var adapter = new TwitchCommandAdapter(new FakeTwitchApi());
        var command = new ParsedCommand(CommandIntent.SetStreamCategory, "Counter-Strike 2");

        var exception = await Assert.ThrowsAsync<TwitchApiException>(
            () => adapter.ExecuteAsync(command));

        Assert.Contains("не проверена", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeTwitchApi : ITwitchApiClient
    {
        public TwitchCategory? Category { get; init; }

        public TwitchChannel Channel { get; init; } =
            new("Текущее название", string.Empty, string.Empty);

        public Exception? ChannelFailure { get; init; }

        public string? LastCategoryQuery { get; private set; }

        public string? LastTitle { get; private set; }

        public string? LastGameId { get; private set; }

        public Task<TwitchUser> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TwitchUser("user-1", "streamer", "Streamer"));

        public Task<TwitchChannel> GetChannelAsync(CancellationToken cancellationToken = default) =>
            ChannelFailure is null
                ? Task.FromResult(Channel)
                : Task.FromException<TwitchChannel>(ChannelFailure);

        public Task<TwitchStreamStatus> GetStreamStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TwitchStreamStatus(IsLive: false, Viewers: 0));

        public Task<TwitchCategory?> FindFirstCategoryAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            LastCategoryQuery = query;
            return Task.FromResult(Category);
        }

        public Task UpdateTitleAsync(string title, CancellationToken cancellationToken = default)
        {
            LastTitle = title;
            return Task.CompletedTask;
        }

        public Task UpdateCategoryAsync(string gameId, CancellationToken cancellationToken = default)
        {
            LastGameId = gameId;
            return Task.CompletedTask;
        }
    }
}
