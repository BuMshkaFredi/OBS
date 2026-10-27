using ObsVoiceAssistant.Recordings;

namespace ObsVoiceAssistant.UnitTests;

public sealed class RecordingFileNameTests
{
    [Fact]
    public void BuildSiblingPath_KeepsNameAndReplacesExtension()
    {
        var source = @"C:\Recordings\2026-10-20 18-16-32.mkv";

        var target = RecordingFileName.BuildSiblingPath(source, ".mp4", _ => false);

        Assert.Equal(@"C:\Recordings\2026-10-20 18-16-32.mp4", target);
    }

    [Fact]
    public void BuildSiblingPath_WhenTargetExists_AddsNumber()
    {
        var source = @"C:\Recordings\2026-10-20 18-16-32.mkv";

        var target = RecordingFileName.BuildSiblingPath(
            source,
            ".mp4",
            path => path.EndsWith("32.mp4", StringComparison.Ordinal));

        Assert.Equal(@"C:\Recordings\2026-10-20 18-16-32 (2).mp4", target);
    }
    [Theory]
    [InlineData("Мой клип", "Мой клип")]
    [InlineData("  Мой клип  ", "Мой клип")]
    [InlineData("Мой клип...", "Мой клип")]
    [InlineData("Мой/клип:2026", "Мой_клип_2026")]
    [InlineData("клип.mp4", "клип")]
    [InlineData("клип.MKV", "клип")]
    [InlineData("клип.webm", "клип")]
    public void SanitizeName_KeepsValidCharactersAndTrimsEdges(string value, string expected)
    {
        Assert.Equal(expected, RecordingFileName.SanitizeName(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(null)]
    public void SanitizeName_ReturnsEmptyWhenNothingRemains(string? value)
    {
        Assert.Equal(string.Empty, RecordingFileName.SanitizeName(value));
    }

    [Fact]
    public void SanitizeName_LimitsLength()
    {
        var value = new string('а', RecordingFileName.MaxNameLength + 20);

        var sanitized = RecordingFileName.SanitizeName(value);

        Assert.Equal(RecordingFileName.MaxNameLength, sanitized.Length);
    }

    [Fact]
    public void BuildTargetPath_ReplacesObsNameAndKeepsExtension()
    {
        var source = @"C:\Recordings\2026-10-20 18-16-32.mkv";

        var target = RecordingFileName.BuildTargetPath(source, "Мой клип", _ => false);

        Assert.Equal(@"C:\Recordings\Мой клип.mkv", target);
    }

    [Fact]
    public void BuildTargetPath_WhenNameIsTaken_AddsNumberInsteadOfOverwriting()
    {
        var source = @"C:\Recordings\2026-10-20 18-16-32.mkv";
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Recordings\Мой клип.mkv",
            @"C:\Recordings\Мой клип (2).mkv"
        };

        var target = RecordingFileName.BuildTargetPath(source, "Мой клип", taken.Contains);

        Assert.Equal(@"C:\Recordings\Мой клип (3).mkv", target);
    }
}
