using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Speech;

namespace ObsVoiceAssistant.UnitTests;

public sealed class WakeWordSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WakeWordWithCommand_ReturnsCommandWithoutWakeWord()
    {
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("Бочи, включи микрофон");

        Assert.Equal(WakeWordDecisionKind.Command, decision.Kind);
        Assert.Equal("включи микрофон", decision.CommandText);
    }

    [Fact]
    public void WakeWordAlone_ArmsNextUtterance()
    {
        var session = new WakeWordSession(clock: () => Start);

        var wake = session.Handle("Бочи");
        var command = session.Handle("выключи музыку");

        Assert.Equal(WakeWordDecisionKind.Armed, wake.Kind);
        Assert.Equal(WakeWordDecisionKind.Command, command.Kind);
        Assert.Equal("выключи музыку", command.CommandText);
    }

    [Fact]
    public void WakeWordAlone_ExpiresAfterCommandWindow()
    {
        var now = Start;
        var session = new WakeWordSession(TimeSpan.FromSeconds(6), () => now);

        session.Handle("Бочи");
        now = now.AddSeconds(7);

        var decision = session.Handle("выключи музыку");

        Assert.Equal(WakeWordDecisionKind.Ignored, decision.Kind);
    }

    [Fact]
    public void CommandWithoutWakeWord_IsIgnored()
    {
        // Фоновая речь без обращения к помощнику не должна ничего выполнять.
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("выключи микрофон");

        Assert.Equal(WakeWordDecisionKind.Ignored, decision.Kind);
        Assert.Equal(string.Empty, decision.CommandText);
    }

    [Theory]
    [InlineData("бачи")]
    [InlineData("боци")]
    [InlineData("бочу")]
    [InlineData("бочм")]
    public void OneSymbolError_StillActivates(string variant)
    {
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle($"{variant} начни запись");

        Assert.Equal(WakeWordDecisionKind.Command, decision.Kind);
        Assert.Equal("начни запись", decision.CommandText);
    }

    [Fact]
    public void ImmediatePhrase_WithoutPause_IsParsedAsCommand()
    {
        // Ровно тот случай, который проверял пользователь: ключевое слово и команда
        // в одной фразе, без паузы и без отдельного уведомления «Слушаю».
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("Бочи выключи микрофон");
        var parsed = new CommandParser().Parse(decision.CommandText);

        Assert.Equal(WakeWordDecisionKind.Command, decision.Kind);
        Assert.Equal("выключи микрофон", decision.CommandText);
        Assert.NotNull(parsed);
        Assert.Equal(CommandIntent.ToggleInput, parsed.Intent);
    }

    [Fact]
    public void OneSymbolError_BoteWithReplayCommand_Activates()
    {
        // «Боте» — одна ошибка от «боче», команда при этом должна дойти целиком.
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("боте сохрани повтор");
        var parsed = new CommandParser().Parse(decision.CommandText);

        Assert.Equal(WakeWordDecisionKind.Command, decision.Kind);
        Assert.Equal("сохрани повтор", decision.CommandText);
        Assert.NotNull(parsed);
        Assert.Equal(CommandIntent.SaveReplay, parsed.Intent);
    }

    [Fact]
    public void OneSymbolError_InLongVariant_Activates()
    {
        // «Бочку» — одна ошибка от уже принятого «бочка».
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("бочку начни запись");

        Assert.Equal(WakeWordDecisionKind.Command, decision.Kind);
        Assert.Equal("начни запись", decision.CommandText);
    }

    [Fact]
    public void FirstLetterError_DoesNotActivate()
    {
        // «Ночи» отличается на одну букву, но это обычное слово, а не обращение.
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("ночи начни запись");

        Assert.Equal(WakeWordDecisionKind.Ignored, decision.Kind);
    }

    [Fact]
    public void TwoSymbolErrors_DoNotActivate()
    {
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("бача начни запись");

        Assert.Equal(WakeWordDecisionKind.Ignored, decision.Kind);
    }

    [Fact]
    public void WakeWordInMiddle_DoesNotActivate()
    {
        // «Бочки» — обычное слово. Если искать его в середине фразы,
        // фоновая речь будет открывать микрофон.
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("включи микрофон бочки");

        Assert.Equal(WakeWordDecisionKind.Ignored, decision.Kind);
    }

    [Fact]
    public void CommonWordInsidePhrase_DoesNotActivate()
    {
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("на столе стоят бочки");

        Assert.Equal(WakeWordDecisionKind.Ignored, decision.Kind);
    }

    [Fact]
    public void LeadingFiller_BeforeWakeWord_StillActivates()
    {
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle("ну бочки включи микрофон");

        Assert.Equal(WakeWordDecisionKind.Command, decision.Kind);
        Assert.Equal("включи микрофон", decision.CommandText);
    }

    [Fact]
    public void WakeWordWithFillerOnly_OpensCommandWindow()
    {
        // «Бочи, пожалуйста» — обращение без команды, следующая фраза ещё ждётся.
        var session = new WakeWordSession(clock: () => Start);

        var wake = session.Handle("Бочи, пожалуйста");

        Assert.Equal(WakeWordDecisionKind.Armed, wake.Kind);
    }

    [Theory]
    [InlineData("боче")]
    [InlineData("бочий")]
    [InlineData("бочь")]
    [InlineData("бочка")]
    [InlineData("бочки")]
    [InlineData("бочке")]
    [InlineData("бочек")]
    [InlineData("боже")]
    public void RecognitionVariants_Activate(string variant)
    {
        var session = new WakeWordSession(clock: () => Start);

        var decision = session.Handle($"{variant} начни запись");

        Assert.Equal(WakeWordDecisionKind.Command, decision.Kind);
        Assert.Equal("начни запись", decision.CommandText);
    }

    [Fact]
    public void SamePhraseCommand_DoesNotLeaveWindowOpen()
    {
        var session = new WakeWordSession(clock: () => Start);

        session.Handle("Бочи начни запись");
        var next = session.Handle("останови стрим");

        Assert.Equal(WakeWordDecisionKind.Ignored, next.Kind);
    }

    [Fact]
    public void ArmedCommand_IsConsumedOnce()
    {
        var session = new WakeWordSession(clock: () => Start);

        session.Handle("Бочи");
        session.Handle("начни запись");
        var next = session.Handle("начни запись");

        Assert.Equal(WakeWordDecisionKind.Ignored, next.Kind);
    }

    [Fact]
    public void Reset_ClosesCommandWindow()
    {
        // Push-to-talk выполнил команду сам: окно после «Бочи» больше не нужно.
        var session = new WakeWordSession(clock: () => Start);

        session.Handle("Бочи");
        session.Reset();
        var decision = session.Handle("выключи музыку");

        Assert.Equal(WakeWordDecisionKind.Ignored, decision.Kind);
    }

    [Fact]
    public void EmptyText_IsIgnored()
    {
        var session = new WakeWordSession(clock: () => Start);

        Assert.Equal(WakeWordDecisionKind.Ignored, session.Handle(null).Kind);
        Assert.Equal(WakeWordDecisionKind.Ignored, session.Handle("   ").Kind);
    }
}
