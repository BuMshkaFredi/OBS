using ObsVoiceAssistant.Application;

namespace ObsVoiceAssistant.UnitTests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void SecondGuard_WithSameName_IsNotPrimary()
    {
        var name = NewName();

        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
    }

    [Fact]
    public void Guard_WithDifferentNames_IsPrimary()
    {
        using var first = new SingleInstanceGuard(NewName());
        using var second = new SingleInstanceGuard(NewName());

        Assert.True(first.IsPrimary);
        Assert.True(second.IsPrimary);
    }

    [Fact]
    public void Guard_AfterFirstDisposed_BecomesPrimary()
    {
        var name = NewName();
        var first = new SingleInstanceGuard(name);
        Assert.True(first.IsPrimary);

        first.Dispose();

        using var second = new SingleInstanceGuard(name);
        Assert.True(second.IsPrimary);
    }

    private static string NewName() => $@"Local\ObsVoiceAssistant.Tests.{Guid.NewGuid():N}";
}
