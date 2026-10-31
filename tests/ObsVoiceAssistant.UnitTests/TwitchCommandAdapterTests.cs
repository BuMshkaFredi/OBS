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
        Assert.Contains("кс 2", api.Queries, StringComparer.Ordinal);
    }

    [Fact]
    public async Task PrepareCategory_TriesTransliterationWhenRussianQueryFindsNothing()
    {
        var api = new FakeTwitchApi
        {
            Category = new TwitchCategory("game-42", "Dota 2", null),
            // Первый (русский) запрос ничего не находит, латинский — находит.
            OnlyQueryFound = "dota 2"
        };
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(CommandIntent.SetStreamCategory, "дота два");

        var prepared = await adapter.PrepareAsync(command);

        Assert.Equal("Dota 2", prepared.Argument);
        Assert.Equal("game-42", prepared.ArgumentId);
        Assert.Contains("дота два", api.Queries, StringComparer.Ordinal);
        Assert.Contains("dota 2", api.Queries, StringComparer.Ordinal);
    }

    [Fact]
    public async Task PrepareCategory_RussianGameName_SearchesAliasDirectly()
    {
        // «Пас оф экзайл» транслитерация не находит: категория берётся из таблицы
        // известных игр, и русский запрос на Twitch не отправляется.
        var api = new FakeTwitchApi
        {
            Category = new TwitchCategory("poe-1", "Path of Exile", null),
            OnlyQueryFound = "Path of Exile"
        };
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(CommandIntent.SetStreamCategory, "пас оф экзайл");

        var prepared = await adapter.PrepareAsync(command);

        Assert.Equal("Path of Exile", prepared.Argument);
        Assert.Equal("poe-1", prepared.ArgumentId);
        Assert.Equal(new[] { "Path of Exile" }, api.Queries);
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
        Assert.Empty(api.Queries);
    }

    [Fact]
    public async Task ExecuteCategory_UsesResolvedGameId()
    {
        var api = new FakeTwitchApi { Channel = new TwitchChannel(string.Empty, "Dota 2", "game-1") };
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(
            CommandIntent.SetStreamCategory,
            "Dota 2",
            ArgumentId: "game-1");

        var result = await adapter.ExecuteAsync(command);

        Assert.Equal("game-1", api.LastGameId);
        Assert.Contains("Dota 2", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteCategory_WhenTwitchDidNotApplyChange_ReportsActualCategory()
    {
        // Helix ответил успехом, но категория осталась прежней: молчать нельзя.
        var api = new FakeTwitchApi
        {
            Channel = new TwitchChannel(string.Empty, "Just Chatting", "game-old")
        };
        var adapter = new TwitchCommandAdapter(api);
        var command = new ParsedCommand(
            CommandIntent.SetStreamCategory,
            "Dota 2",
            ArgumentId: "game-1");

        var exception = await Assert.ThrowsAsync<TwitchApiException>(
            () => adapter.ExecuteAsync(command));

        Assert.Contains("Just Chatting", exception.Message, StringComparison.Ordinal);
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

        public string? OnlyQueryFound { get; init; }

        public TwitchChannel Channel { get; init; } =
            new(string.Empty, string.Empty, string.Empty);

        public Exception? ChannelFailure { get; init; }

        public List<string> Queries { get; } = new();

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
            Queries.Add(query);
            var found = OnlyQueryFound is null ||
                        string.Equals(query, OnlyQueryFound, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(found ? Category : null);
        }

        public Task UpdateCategoryAsync(string gameId, CancellationToken cancellationToken = default)
        {
            LastGameId = gameId;
            return Task.CompletedTask;
        }
    }
}
