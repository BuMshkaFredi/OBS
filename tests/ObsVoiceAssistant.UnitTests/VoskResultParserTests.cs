using ObsVoiceAssistant.Speech;

namespace ObsVoiceAssistant.UnitTests;

public sealed class VoskResultParserTests
{
    [Fact]
    public void ParseText_WithRecognizedCommand_ReturnsTrimmedText()
    {
        var text = VoskResultParser.ParseText("{\"text\": \"  начни запись  \"}");

        Assert.Equal("начни запись", text);
    }

    [Fact]
    public void ParseText_WithEmptyText_ReturnsNull()
    {
        Assert.Null(VoskResultParser.ParseText("{\"text\": \"\"}"));
        Assert.Null(VoskResultParser.ParseText("{\"text\": \"   \"}"));
    }

    [Fact]
    public void ParseText_WithoutTextProperty_ReturnsNull()
    {
        Assert.Null(VoskResultParser.ParseText("{\"result\": [{\"word\": \"начни\"}]}"));
    }

    [Fact]
    public void ParseText_WithNonStringText_ReturnsNull()
    {
        Assert.Null(VoskResultParser.ParseText("{\"text\": 42}"));
    }

    [Fact]
    public void ParseText_WithInvalidJson_ReturnsNull()
    {
        Assert.Null(VoskResultParser.ParseText("не json"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseText_WithMissingPayload_ReturnsNull(string? json)
    {
        Assert.Null(VoskResultParser.ParseText(json));
    }
}
