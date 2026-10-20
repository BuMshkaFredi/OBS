using ObsVoiceAssistant.Configuration;

namespace ObsVoiceAssistant.UnitTests;

public sealed class ObsInputSelectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(ObsInputSelection.NoneLabel)]
    [InlineData("  — не использовать —  ")]
    public void ToInputName_WhenRoleHasNoSource_ReturnsNull(string? value)
    {
        Assert.Null(ObsInputSelection.ToInputName(value));
    }

    [Theory]
    [InlineData("Mic/Aux", "Mic/Aux")]
    [InlineData("  Mic/Aux  ", "Mic/Aux")]
    [InlineData("Звук рабочего стола", "Звук рабочего стола")]
    public void ToInputName_WhenSourceIsSet_ReturnsTrimmedName(string value, string expected)
    {
        Assert.Equal(expected, ObsInputSelection.ToInputName(value));
    }

    [Fact]
    public void BuildChoices_StartsWithNoneOption()
    {
        var choices = ObsInputSelection.BuildChoices(new[] { "Mic/Aux", "Браузер" });

        Assert.Equal(
            new[] { ObsInputSelection.NoneLabel, "Mic/Aux", "Браузер" },
            choices);
    }

    [Fact]
    public void BuildChoices_WithoutObsConnection_StillAllowsNoSource()
    {
        var choices = ObsInputSelection.BuildChoices(null);

        Assert.Equal(new[] { ObsInputSelection.NoneLabel }, choices);
    }

    [Fact]
    public void BuildChoices_SkipsEmptyNamesAndDuplicateNoneOption()
    {
        var choices = ObsInputSelection.BuildChoices(
            new[] { ObsInputSelection.NoneLabel, "Mic/Aux", "  " });

        Assert.Equal(new[] { ObsInputSelection.NoneLabel, "Mic/Aux" }, choices);
    }

    [Fact]
    public void BuildChoices_SameSourceIsAvailableForEveryRole()
    {
        // Поля независимы: звук рабочего стола можно указать и как музыку стрима, и как личную.
        var streamMusic = ObsInputSelection.BuildChoices(new[] { "Звук рабочего стола" });
        var personalMusic = ObsInputSelection.BuildChoices(new[] { "Звук рабочего стола" });

        Assert.Contains("Звук рабочего стола", streamMusic);
        Assert.Contains("Звук рабочего стола", personalMusic);
    }
}
