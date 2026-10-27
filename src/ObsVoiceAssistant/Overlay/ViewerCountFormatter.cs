using System.Globalization;

namespace ObsVoiceAssistant.Overlay;

/// <summary>Форматирование числа и русское склонение слова «зритель».</summary>
public static class ViewerCountFormatter
{
    private static readonly NumberFormatInfo CountFormat = new()
    {
        NumberGroupSeparator = " ",
        NumberGroupSizes = new[] { 3 },
        NumberDecimalDigits = 0
    };

    public static string FormatCount(int viewers) =>
        Math.Max(0, viewers).ToString("N0", CountFormat);

    public static string FormatLabel(int viewers)
    {
        var lastTwo = Math.Abs(viewers) % 100;
        if (lastTwo is >= 11 and <= 14)
        {
            return "зрителей";
        }

        return (lastTwo % 10) switch
        {
            1 => "зритель",
            >= 2 and <= 4 => "зрителя",
            _ => "зрителей"
        };
    }

    public static string Format(int viewers) => $"{FormatCount(viewers)} {FormatLabel(viewers)}";
}
