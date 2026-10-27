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
        var command = new ParsedCommand(CommandIntent.SetStreamTitle, "Название");

        var prepared = await adapter.PrepareAsync(command);

        Assert.Same(command, prepared);
        Assert.Null(api.LastCategoryQuery);
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

        public string? LastCategoryQuery { get; private set; }

        public string? LastTitle { get; private set; }

        public string? LastGameId { get; private set; }

        public Task<TwitchUser> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TwitchUser("user-1", "streamer", "Streamer"));

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
