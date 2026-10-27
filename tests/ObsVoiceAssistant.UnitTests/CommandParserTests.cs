using ObsVoiceAssistant.Commands;

namespace ObsVoiceAssistant.UnitTests;

public sealed class CommandParserTests
{
    private readonly CommandParser parser = new();

    [Theory]
    [InlineData("начни запись", CommandIntent.StartRecording)]
    [InlineData("запусти запись", CommandIntent.StartRecording)]
    [InlineData("Сохраним повтор.", CommandIntent.SaveReplay)]
    [InlineData("начну запись", CommandIntent.StartRecording)]
    [InlineData("  ВЫКЛЮЧИ   МИКРОФОН ", CommandIntent.ToggleInput)]
    [InlineData("выключи микрофон!", CommandIntent.ToggleInput)]
    [InlineData("включи микрофон", CommandIntent.ToggleInput)]
    [InlineData("останови трансляцию", CommandIntent.StopStream)]
    [InlineData("ночи стрим", CommandIntent.StartStream)]
    public void Parse_KnownPhrase_ReturnsExpectedIntent(string text, CommandIntent expected)
    {
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
    }

    [Fact]
    public void Parse_MicrophoneCommand_SetsMicrophoneTarget()
    {
        var result = parser.Parse("выключи микрофон");

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.ToggleInput, result.Intent);
        Assert.Equal("microphone", result.Target);
    }

    [Fact]
    public void Parse_MicrophoneOnAndOff_ProduceSameCommand()
    {
        // Микрофон работает как переключатель: слово в команде не выбирает состояние.
        Assert.Equal(parser.Parse("включи микрофон"), parser.Parse("выключи микрофон"));
    }

    [Fact]
    public void Parse_Title_ExtractsArgumentIgnoringPunctuation()
    {
        var result = parser.Parse("измени название стрима на Вечерний ranked.");

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.SetStreamTitle, result.Intent);
        Assert.Equal("вечерний ranked", result.Argument);
    }

    [Fact]
    public void Parse_Category_ExtractsArgument()
    {
        var result = parser.Parse("измени категорию стрима на Just Chatting");

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.SetStreamCategory, result.Intent);
        Assert.Equal("just chatting", result.Argument);
    }

    [Fact]
    public void Parse_LoneStreamWord_ReturnsNull()
    {
        // Одиночное слово опасно: оно не должно запускать или останавливать трансляцию.
        Assert.Null(parser.Parse("стрим."));
    }

    [Fact]
    public void Parse_GarbledTranscription_ReturnsNull()
    {
        // Искажённое распознавание не угадывается: точность поднимаем на уровне модели,
        // а не скрытым fuzzy matching.
        Assert.Null(parser.Parse("сэфрениповтор"));
    }

    [Fact]
    public void Parse_UnknownPhrase_ReturnsNull()
    {
        Assert.Null(parser.Parse("сделай что-нибудь"));
    }
}

public sealed class ConfirmationPolicyTests
{
    private readonly ConfirmationPolicy policy = new();

    [Theory]
    [InlineData(CommandIntent.StartStream)]
    [InlineData(CommandIntent.StopRecording)]
    [InlineData(CommandIntent.StopStream)]
    [InlineData(CommandIntent.SetStreamTitle)]
    [InlineData(CommandIntent.SetStreamCategory)]
    public void DangerousCommands_RequireConfirmation(CommandIntent intent)
    {
        Assert.True(policy.RequiresConfirmation(intent));
    }

    [Theory]
    [InlineData(CommandIntent.StartRecording)]
    [InlineData(CommandIntent.SaveReplay)]
    [InlineData(CommandIntent.MuteInput)]
    [InlineData(CommandIntent.ToggleInput)]
    [InlineData(CommandIntent.MuteAllAudio)]
    [InlineData(CommandIntent.AdjustInputVolume)]
    public void SafeCommands_DoNotRequireConfirmation(CommandIntent intent)
    {
        Assert.False(policy.RequiresConfirmation(intent));
    }
}
