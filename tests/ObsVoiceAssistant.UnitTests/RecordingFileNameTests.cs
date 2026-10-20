using ObsVoiceAssistant.Recordings;

namespace ObsVoiceAssistant.UnitTests;

public sealed class RecordingFileNameTests
{
    [Theory]
    [InlineData("Мой клип", "Мой клип")]
    [InlineData("  Мой клип  ", "Мой клип")]
    [InlineData("Мой клип...", "Мой клип")]
    [InlineData("Мой/клип:2026", "Мой_клип_2026")]
    public void SanitizePrefix_KeepsValidCharactersAndTrimsEdges(string value, string expected)
    {
        Assert.Equal(expected, RecordingFileName.SanitizePrefix(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(null)]
    public void SanitizePrefix_ReturnsEmptyWhenNothingRemains(string? value)
    {
        Assert.Equal(string.Empty, RecordingFileName.SanitizePrefix(value));
    }

    [Fact]
    public void SanitizePrefix_LimitsLength()
    {
        var value = new string('а', RecordingFileName.MaxPrefixLength + 20);

        var sanitized = RecordingFileName.SanitizePrefix(value);

        Assert.Equal(RecordingFileName.MaxPrefixLength, sanitized.Length);
    }

    [Fact]
    public void BuildTargetPath_PutsPrefixBeforeObsNameAndKeepsExtension()
    {
        var source = @"C:\Recordings\2026-10-20 18-16-32.mkv";

        var target = RecordingFileName.BuildTargetPath(source, "Мой клип", _ => false);

        Assert.Equal(@"C:\Recordings\Мой клип_2026-10-20 18-16-32.mkv", target);
    }

    [Fact]
    public void BuildTargetPath_WhenNameIsTaken_AddsNumberInsteadOfOverwriting()
    {
        var source = @"C:\Recordings\2026-10-20 18-16-32.mkv";
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Recordings\Мой клип_2026-10-20 18-16-32.mkv",
            @"C:\Recordings\Мой клип_2026-10-20 18-16-32 (2).mkv"
        };

        var target = RecordingFileName.BuildTargetPath(source, "Мой клип", taken.Contains);

        Assert.Equal(@"C:\Recordings\Мой клип_2026-10-20 18-16-32 (3).mkv", target);
    }
}
