using System.IO;
using ObsVoiceAssistant.Integrations.Obs;
using ObsVoiceAssistant.Recordings;

namespace ObsVoiceAssistant.UnitTests;

public sealed class RecordingNamingServiceTests
{
    private sealed class StubPrompt : IRecordingNamePrompt
    {
        private readonly string? answer;

        public StubPrompt(string? answer) => this.answer = answer;

        public ObsSavedFileKind? Kind { get; private set; }

        public string? FileName { get; private set; }

        public bool WasAsked => FileName is not null;

        public Task<string?> AskNameAsync(ObsSavedFileKind kind, string fileName)
        {
            Kind = kind;
            FileName = fileName;
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenPrefixGiven_RenamesFileAndNotifies()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = Path.Combine(directory, "2026-10-20 18-16-32.mkv");
            await File.WriteAllTextAsync(source, "video");
            var notifications = new List<string>();
            var prompt = new StubPrompt("Мой клип");
            var service = new RecordingNamingService(prompt, notifications.Add);

            await service.HandleAsync(new ObsSavedFile(source, ObsSavedFileKind.Recording));

            var renamed = Path.Combine(directory, "Мой клип_2026-10-20 18-16-32.mkv");
            Assert.True(File.Exists(renamed));
            Assert.False(File.Exists(source));
            Assert.Contains(notifications, message => message.Contains("Запись переименована", StringComparison.Ordinal));
            Assert.Equal(ObsSavedFileKind.Recording, prompt.Kind);
            Assert.Equal("2026-10-20 18-16-32.mkv", prompt.FileName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenReplaySaved_UsesSamePrefixRule()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = Path.Combine(directory, "Replay 2026-10-20 18-16-32.mkv");
            await File.WriteAllTextAsync(source, "video");
            var notifications = new List<string>();
            var prompt = new StubPrompt("Лучший момент");
            var service = new RecordingNamingService(prompt, notifications.Add);

            await service.HandleAsync(new ObsSavedFile(source, ObsSavedFileKind.Replay));

            Assert.True(File.Exists(Path.Combine(directory, "Лучший момент_Replay 2026-10-20 18-16-32.mkv")));
            Assert.Contains(notifications, message => message.Contains("Повтор переименован", StringComparison.Ordinal));
            Assert.Equal(ObsSavedFileKind.Replay, prompt.Kind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WhenNameIsEmpty_LeavesObsName(string? answer)
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = Path.Combine(directory, "2026-10-20 18-16-32.mkv");
            await File.WriteAllTextAsync(source, "video");
            var notifications = new List<string>();
            var service = new RecordingNamingService(new StubPrompt(answer), notifications.Add);

            await service.HandleAsync(new ObsSavedFile(source, ObsSavedFileKind.Recording));

            Assert.True(File.Exists(source));
            Assert.Empty(notifications);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenFileNameIsInvalid_SanitizesIt()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = Path.Combine(directory, "Replay 2026-10-20 18-16-32.mkv");
            await File.WriteAllTextAsync(source, "video");
            var service = new RecordingNamingService(new StubPrompt("клип/финал:1"), _ => { });

            await service.HandleAsync(new ObsSavedFile(source, ObsSavedFileKind.Replay));

            Assert.True(File.Exists(Path.Combine(directory, "клип_финал_1_Replay 2026-10-20 18-16-32.mkv")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenTargetExists_AddsNumberInsteadOfOverwriting()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = Path.Combine(directory, "2026-10-20 18-16-32.mkv");
            var existing = Path.Combine(directory, "Мой клип_2026-10-20 18-16-32.mkv");
            await File.WriteAllTextAsync(source, "new");
            await File.WriteAllTextAsync(existing, "old");
            var service = new RecordingNamingService(new StubPrompt("Мой клип"), _ => { });

            await service.HandleAsync(new ObsSavedFile(source, ObsSavedFileKind.Recording));

            Assert.Equal("old", await File.ReadAllTextAsync(existing));
            Assert.True(File.Exists(Path.Combine(directory, "Мой клип_2026-10-20 18-16-32 (2).mkv")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_WhenFileIsMissing_NotifiesAndDoesNotAskForName()
    {
        var directory = CreateTempDirectory();
        try
        {
            var missing = Path.Combine(directory, "2026-10-20 18-16-32.mkv");
            var notifications = new List<string>();
            var prompt = new StubPrompt("Мой клип");
            var service = new RecordingNamingService(prompt, notifications.Add);

            await service.HandleAsync(new ObsSavedFile(missing, ObsSavedFileKind.Recording));

            Assert.False(prompt.WasAsked);
            Assert.Contains(notifications, message => message.Contains("не найден", StringComparison.Ordinal));
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
            "obsva-recording-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
