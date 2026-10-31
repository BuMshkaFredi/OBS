using ObsVoiceAssistant.Audio;

namespace ObsVoiceAssistant.UnitTests;

public sealed class PushToTalkHotkeyTests
{
    [Fact]
    public void Parse_EmptyValue_ReturnsCapsLock()
    {
        Assert.True(PushToTalkHotkey.TryParse(null, out var hotkey, out var error));

        Assert.Null(error);
        Assert.Equal(PushToTalkHotkey.CapsLock, hotkey);
        Assert.Equal("Caps Lock", hotkey.DisplayName);
    }

    [Theory]
    [InlineData("Caps Lock")]
    [InlineData("capslock")]
    [InlineData("капс")]
    public void Parse_CapsLockVariants_ReturnSameHotkey(string value)
    {
        Assert.True(PushToTalkHotkey.TryParse(value, out var hotkey, out var error));

        Assert.Null(error);
        Assert.Equal(PushToTalkHotkey.CapsLockVirtualKey, hotkey.VirtualKey);
        Assert.False(hotkey.HasModifiers);
    }

    [Fact]
    public void Parse_CtrlShiftF9_ReturnsCombination()
    {
        Assert.True(PushToTalkHotkey.TryParse("Ctrl+Shift+F9", out var hotkey, out var error));

        Assert.Null(error);
        Assert.Equal(0x78, hotkey.VirtualKey);
        Assert.True(hotkey.Control);
        Assert.True(hotkey.Shift);
        Assert.False(hotkey.Alt);
        Assert.Equal("Ctrl+Shift+F9", hotkey.DisplayName);
    }

    [Fact]
    public void Parse_SingleKeyWithoutModifiers_IsAccepted()
    {
        Assert.True(PushToTalkHotkey.TryParse("F12", out var hotkey, out var error));

        Assert.Null(error);
        Assert.Equal(0x7B, hotkey.VirtualKey);
        Assert.False(hotkey.HasModifiers);
    }

    [Theory]
    [InlineData("контрол+шифт+F9")]
    [InlineData(" Ctrl + Shift + F9 ")]
    public void Parse_DifferentSpellings_ReturnSameHotkey(string value)
    {
        Assert.True(PushToTalkHotkey.TryParse(value, out var hotkey, out _));

        Assert.Equal("Ctrl+Shift+F9", hotkey.DisplayName);
    }

    [Theory]
    [InlineData("Ctrl")]
    [InlineData("Shift+Alt")]
    [InlineData("ctrl+shift")]
    public void Parse_ModifiersOnly_Rejected(string value)
    {
        Assert.False(PushToTalkHotkey.TryParse(value, out _, out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("Q W")]
    [InlineData("F9+G")]
    public void Parse_TwoTriggerKeys_Rejected(string value)
    {
        Assert.False(PushToTalkHotkey.TryParse(value, out _, out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Parse_UnknownKey_Rejected()
    {
        Assert.False(PushToTalkHotkey.TryParse("неизвестнаяклавиша", out _, out var error));

        Assert.Contains("не удалось распознать", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSettingValue_CapsLock_IsNullSoDefaultStaysClean()
    {
        Assert.Null(PushToTalkHotkey.CapsLock.ToSettingValue());
    }

    [Fact]
    public void ToSettingValue_CustomCombination_RoundTripsThroughParse()
    {
        Assert.True(PushToTalkHotkey.TryParse("Ctrl+Shift+F9", out var hotkey, out _));

        var saved = hotkey.ToSettingValue();
        Assert.NotNull(saved);
        Assert.True(PushToTalkHotkey.TryParse(saved, out var parsed, out _));
        Assert.Equal(hotkey, parsed);
    }
}
