using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.UnitTests;

public sealed class ConfiguredObsInputResolverTests
{
    [Fact]
    public void Resolve_SameSourceForSeveralRoles_ReturnsItForEach()
    {
        var resolver = new ConfiguredObsInputResolver(new ObsAudioInputs(
            StreamMusic: "Звук рабочего стола",
            PersonalMusic: "Звук рабочего стола"));

        Assert.Equal("Звук рабочего стола", resolver.Resolve("stream_music"));
        Assert.Equal("Звук рабочего стола", resolver.Resolve("personal_music"));
    }

    [Fact]
    public void Resolve_ForRoleWithoutSource_ReturnsNull()
    {
        var resolver = new ConfiguredObsInputResolver(new ObsAudioInputs(Microphone: "Mic/Aux"));

        Assert.Null(resolver.Resolve("browser"));
        Assert.Null(resolver.Resolve("unknown"));
    }
}
