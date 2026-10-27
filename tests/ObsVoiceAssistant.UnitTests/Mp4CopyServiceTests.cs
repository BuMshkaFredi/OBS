using System.IO;
using ObsVoiceAssistant.Recordings;

namespace ObsVoiceAssistant.UnitTests;

public sealed class Mp4CopyServiceTests
{
    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly int exitCode;
        private readonly string errorOutput;
        private readonly bool createTargetFile;

        public StubProcessRunner(int exitCode = 0, string errorOutput = "", bool createTargetFile = true)
        {
            this.exitCode = exitCode;
            this.errorOutput = errorOutput;
            this.createTargetFile = createTargetFile;
        }

        public string? ExecutedFile { get; private set; }

        public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();

        public bool WasExecuted => ExecutedFile is not null;

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            ExecutedFile = fileName;
            Arguments = arguments;

            if (createTargetFile && arguments.Count > 0)
            {
                // Настоящий ffmpeg создаёт целевой файл: сервис проверяет его наличие.
                File.WriteAllText(arguments[^1], "mp4");
            }

            return Task.FromResult(new ProcessResult(exitCode, errorOutput));
        }
    }

    [Fact]
    public async Task CreateCopyAsync_WhenDisabled_DoesNothing()
    {
        var runner = new StubProcessRunner();
        var service = CreateService(new Mp4CopyOptions(Enabled: false), runner);

        var result = await service.CreateCopyAsync(@"C:\Recordings\2026-10-21 22-05-19.mkv");

        Assert.Equal(Mp4CopyStatus.Disabled, result.Status);
        Assert.False(runner.WasExecuted);
    }

    [Fact]
    public async Task CreateCopyAsync_WhenSourceIsAlreadyMp4_SkipsConversion()
    {
        var runner = new StubProcessRunner();
        var service = CreateService(new Mp4CopyOptions(), runner);

        var result = await service.CreateCopyAsync(@"C:\Recordings\Replay 2026-10-21.mkv.mp4");

        Assert.Equal(Mp4CopyStatus.AlreadyMp4, result.Status);
        Assert.False(runner.WasExecuted);
    }

    [Fact]
    public async Task CreateCopyAsync_WhenFfmpegMissing_ReportsHowToInstall()
    {
        var runner = new StubProcessRunner();
        var service = CreateService(new Mp4CopyOptions(), runner, locateFfmpeg: _ => null);

        var result = await service.CreateCopyAsync(@"C:\Recordings\2026-10-21 22-05-19.mkv");

        Assert.Equal(Mp4CopyStatus.Failed, result.Status);
        Assert.Contains("ffmpeg", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(runner.WasExecuted);
    }

    [Fact]
    public async Task CreateCopyAsync_WhenFfmpegSucceeds_CreatesMp4NextToSource()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = Path.Combine(directory, "2026-10-21 22-05-19.mkv");
            await File.WriteAllTextAsync(source, "video");
            var runner = new StubProcessRunner();
            var service = CreateService(new Mp4CopyOptions(), runner);

            var result = await service.CreateCopyAsync(source);

            Assert.Equal(Mp4CopyStatus.Created, result.Status);
            Assert.NotNull(result.TargetPath);
            Assert.Equal(Path.Combine(directory, "2026-10-21 22-05-19.mp4"), result.TargetPath);
            Assert.True(File.Exists(result.TargetPath));

            // Потоки копируются без перекодирования: качество не меняется.
            var arguments = runner.Arguments;
            Assert.Contains("-i", arguments);
            Assert.Contains("-c", arguments);
            Assert.Contains("copy", arguments);
            Assert.Contains(source, arguments);
            Assert.Contains(result.TargetPath, arguments);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCopyAsync_WhenTargetExists_AddsNumberInsteadOfOverwriting()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = Path.Combine(directory, "2026-10-21 22-05-19.mkv");
            await File.WriteAllTextAsync(source, "video");
            await File.WriteAllTextAsync(Path.Combine(directory, "2026-10-21 22-05-19.mp4"), "old");
            var service = CreateService(new Mp4CopyOptions(), new StubProcessRunner());

            var result = await service.CreateCopyAsync(source);

            Assert.Equal(Mp4CopyStatus.Created, result.Status);
            Assert.Equal(Path.Combine(directory, "2026-10-21 22-05-19 (2).mp4"), result.TargetPath);
            Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(directory, "2026-10-21 22-05-19.mp4")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateCopyAsync_WhenFfmpegFails_ReportsLastErrorLine()
    {
        var runner = new StubProcessRunner(
            exitCode: 1,
            errorOutput: "some warning\nInvalid data found when processing input\n",
            createTargetFile: false);
        var service = CreateService(new Mp4CopyOptions(), runner);

        var result = await service.CreateCopyAsync(@"C:\Recordings\2026-10-21 22-05-19.mkv");

        Assert.Equal(Mp4CopyStatus.Failed, result.Status);
        Assert.Equal("Invalid data found when processing input", result.Error);
    }

    [Fact]
    public async Task CreateCopyAsync_WhenFfmpegSucceedsButFileIsMissing_ReportsFailure()
    {
        var runner = new StubProcessRunner(createTargetFile: false);
        var service = CreateService(new Mp4CopyOptions(), runner);

        var result = await service.CreateCopyAsync(@"C:\Recordings\2026-10-21 22-05-19.mkv");

        Assert.Equal(Mp4CopyStatus.Failed, result.Status);
        Assert.Contains("не появился", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static FfmpegMp4CopyService CreateService(
        Mp4CopyOptions options,
        IProcessRunner runner,
        Func<string?, string?>? locateFfmpeg = null) =>
        new(
            () => options,
            runner,
            locateFfmpeg: locateFfmpeg ?? (_ => @"C:\tools\ffmpeg.exe"));

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "obsva-mp4-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
