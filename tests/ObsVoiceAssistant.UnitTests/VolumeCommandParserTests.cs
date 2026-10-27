using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Speech;

namespace ObsVoiceAssistant.UnitTests;

public sealed class VolumeCommandParserTests
{
    private readonly CommandParser parser = new();

    [Theory]
    [InlineData("громкость микрофона плюс 10", "microphone", 10)]
    [InlineData("громкость музыки минус 5", "personal_music", -5)]
    [InlineData("громкость музыки +10", "personal_music", 10)]
    [InlineData("громкость -5", "microphone", -5)]
    [InlineData("громкость +10", "microphone", 10)]
    [InlineData("громкость браузера плюс пятнадцать", "browser", 15)]
    [InlineData("громкость музыки стрима минус десять", "stream_music", -10)]
    [InlineData("громкость музыки стрела минус десять", "stream_music", -10)]
    [InlineData("громкость стрела минус десять", "stream_music", -10)]
    [InlineData("громкость стрима минус 10", "stream_music", -10)]
    [InlineData("громкость стрим плюс 10", "stream_music", 10)]
    [InlineData("громкость звука стрима плюс 5", "stream_music", 5)]
    [InlineData("громкость моей музыки минус 5", "personal_music", -5)]
    [InlineData("громкость личной музыки плюс 10 процентов", "personal_music", 10)]
    [InlineData("громкость микрофона плюс десять процентов", "microphone", 10)]
    [InlineData("чем громкость плюс десять", "microphone", 10)]
    [InlineData("меньше громкость микрофоном", "microphone", -10)]
    [InlineData("тише громкость музыки", "personal_music", -10)]
    [InlineData("больше громкость браузером", "browser", 10)]
    [InlineData("громкость микрофона громче", "microphone", 10)]
    [InlineData("громкость музыки тише", "personal_music", -10)]
    [InlineData("громкость музыки плюс", "personal_music", 10)]
    [InlineData("громкость микрофона минус ноль", "microphone", 0)]
    [InlineData("громкость микрофона плюс сто", "microphone", 100)]
    [InlineData("громкость плюс сто", "microphone", 100)]
    [InlineData("громкость плюс стал", "microphone", 100)]
    [InlineData("меньше громкость микрофонов", "microphone", -10)]
    [InlineData("уменьши громкость микрофона", "microphone", -10)]
    [InlineData("громкость микрофона плюс девяносто девять", "microphone", 99)]
    [InlineData("громкость микрофона плюс десеть", "microphone", 10)]
    [InlineData("громкость музыки минус деветь", "personal_music", -9)]
    [InlineData("громкость браузера плюс двацать пять", "browser", 25)]
    [InlineData("громкость музыки плюс петь", "personal_music", 5)]
    [InlineData("громкость музыки пожалуйста плюс 10", "personal_music", 10)]
    public void Parse_VolumeCommand_ReturnsTargetAndDelta(
        string phrase,
        string expectedTarget,
        int expectedDelta)
    {
        var command = parser.Parse(phrase);

        Assert.NotNull(command);
        Assert.Equal(CommandIntent.AdjustInputVolume, command.Intent);
        Assert.Equal(expectedTarget, command.Target);
        Assert.Equal(expectedDelta, command.Delta);
    }

    [Theory]
    [InlineData("громкость")]
    [InlineData("громкость микрофона")]
    [InlineData("громкость микрофона 50")]
    [InlineData("громкость музыки 10")]
    [InlineData("громкость настольного звука плюс 10")]
    [InlineData("громкость микрофона плюс сто пятьдесят")]
    [InlineData("громкость микрофона плюс 101")]
    [InlineData("громкость микрофона плюс 10 наверное")]
    [InlineData("громкость микрофона плюс дехать")]
    [InlineData("не громкость микрофона плюс 10")]
    public void Parse_InvalidVolumePhrase_IsNotRecognized(string phrase)
    {
        Assert.Null(parser.Parse(phrase));
    }

    [Fact]
    public void Parse_MisrecognizedWord_IsCorrectedAndLogged()
    {
        var result = parser.ParseWithDetails("громкасть музыки плюс 10");

        Assert.NotNull(result.Command);
        Assert.Equal(CommandIntent.AdjustInputVolume, result.Command.Intent);
        Assert.Equal("personal_music", result.Command.Target);
        Assert.Equal(10, result.Command.Delta);
        Assert.Contains(result.Corrections, correction =>
            correction.From == "громкасть" && correction.To == "громкость");
    }

    [Fact]
    public void Parse_MisrecognizedTargetCase_StillSelectsSameTarget()
    {
        var command = parser.Parse("громкость музыке плюс 10");

        Assert.NotNull(command);
        Assert.Equal("personal_music", command.Target);
        Assert.Equal(10, command.Delta);
    }

    [Fact]
    public void Parse_StreamMisrecognizedAsArrow_IsCorrectedAndLogged()
    {
        var result = parser.ParseWithDetails("громкость музыки стрела минус десять");

        Assert.NotNull(result.Command);
        Assert.Equal(CommandIntent.AdjustInputVolume, result.Command.Intent);
        Assert.Equal("stream_music", result.Command.Target);
        Assert.Equal(-10, result.Command.Delta);
        Assert.Contains(result.Corrections, correction =>
            correction.From == "стрела" && correction.To == "стрима");
    }

    [Fact]
    public void Parse_NumberMisrecognizedAsStal_IsCorrectedAndLogged()
    {
        var result = parser.ParseWithDetails("громкость плюс стал");

        Assert.NotNull(result.Command);
        Assert.Equal(CommandIntent.AdjustInputVolume, result.Command.Intent);
        Assert.Equal("microphone", result.Command.Target);
        Assert.Equal(100, result.Command.Delta);
        Assert.Contains(result.Corrections, correction =>
            correction.From == "стал" && correction.To == "сто");
    }

    [Fact]
    public void WakeWordRemainder_IsParsedAsVolumeCommand()
    {
        var decision = new WakeWordSession().Handle("Бочи громкость музыки плюс 10");

        var command = parser.Parse(decision.CommandText);

        Assert.Equal(WakeWordDecisionKind.Command, decision.Kind);
        Assert.NotNull(command);
        Assert.Equal(CommandIntent.AdjustInputVolume, command.Intent);
        Assert.Equal("personal_music", command.Target);
        Assert.Equal(10, command.Delta);
    }
}
