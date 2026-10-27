using ObsVoiceAssistant.Overlay;

namespace ObsVoiceAssistant.UnitTests;

public sealed class ViewerCountFormatterTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(1234, "1 234")]
    [InlineData(1234567, "1 234 567")]
    public void FormatCount_UsesSpaceAsThousandsSeparator(int viewers, string expected)
    {
        Assert.Equal(expected, ViewerCountFormatter.FormatCount(viewers));
    }

    [Theory]
    [InlineData(1, "зритель")]
    [InlineData(2, "зрителя")]
    [InlineData(4, "зрителя")]
    [InlineData(5, "зрителей")]
    [InlineData(11, "зрителей")]
    [InlineData(12, "зрителей")]
    [InlineData(14, "зрителей")]
    [InlineData(21, "зритель")]
    [InlineData(22, "зрителя")]
    [InlineData(25, "зрителей")]
    [InlineData(111, "зрителей")]
    [InlineData(121, "зритель")]
    public void FormatLabel_UsesRussianPluralRules(int viewers, string expected)
    {
        Assert.Equal(expected, ViewerCountFormatter.FormatLabel(viewers));
    }
}
