using System.Globalization;
using System.Text;

namespace ObsVoiceAssistant.Audio;

/// <summary>
/// Клавиша или комбинация клавиш push-to-talk. По умолчанию — Caps Lock: так
/// приложение работало до появления настройки, и это поведение остаётся
/// основным. Строка читается из настроек, поэтому разбор вынесен из хука и
/// проверяется unit-тестами без установки глобального обработчика.
/// </summary>
public sealed record PushToTalkHotkey(int VirtualKey, bool Control, bool Shift, bool Alt, bool Windows)
{
    /// <summary>Виртуальный код Caps Lock.</summary>
    public const int CapsLockVirtualKey = 0x14;

    public static PushToTalkHotkey CapsLock { get; } = new(CapsLockVirtualKey, false, false, false, false);

    public bool HasModifiers => Control || Shift || Alt || Windows;

    /// <summary>
    /// Значение для настроек: <c>null</c> для Caps Lock, чтобы файл настроек
    /// оставался чистым, а поведение по умолчанию — очевидным.
    /// </summary>
    public string? ToSettingValue() =>
        Equals(CapsLock) ? null : DisplayName;

    /// <summary>
    /// Текст для настроек и уведомлений: «Caps Lock», «Ctrl+Shift+F9».
    /// </summary>
    public string DisplayName
    {
        get
        {
            var builder = new StringBuilder();
            if (Control)
            {
                builder.Append("Ctrl+");
            }

            if (Shift)
            {
                builder.Append("Shift+");
            }

            if (Alt)
            {
                builder.Append("Alt+");
            }

            if (Windows)
            {
                builder.Append("Win+");
            }

            return builder.Append(DescribeKey(VirtualKey)).ToString();
        }
    }

    /// <summary>
    /// Разбирает строку из настроек. Пустая строка и <c>null</c> дают Caps Lock,
    /// а не ошибку: у пользователя, который ничего не настраивал, всё работает.
    /// </summary>
    public static bool TryParse(string? value, out PushToTalkHotkey hotkey, out string? error)
    {
        hotkey = CapsLock;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var parts = value
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return true;
        }

        var control = false;
        var shift = false;
        var alt = false;
        var windows = false;
        var keyCode = 0;

        foreach (var part in parts)
        {
            if (TryParseModifier(part, out var modifier))
            {
                switch (modifier)
                {
                    case Modifier.Control:
                        control = true;
                        break;
                    case Modifier.Shift:
                        shift = true;
                        break;
                    case Modifier.Alt:
                        alt = true;
                        break;
                    default:
                        windows = true;
                        break;
                }

                continue;
            }

            if (keyCode != 0)
            {
                error = "В комбинации должна быть ровно одна клавиша, не считая Ctrl, Shift, Alt и Win.";
                return false;
            }

            if (!TryParseKey(part, out keyCode))
            {
                error = $"Не удалось распознать клавишу «{part}».";
                return false;
            }
        }

        if (keyCode == 0)
        {
            error = "В комбинации нет клавиши: укажите, например, F9 или Ctrl+Shift+F9.";
            return false;
        }

        // Одиночный модификатор не может быть клавишей push-to-talk: Ctrl сам по себе
        // используется почти во всех сочетаниях и запись включалась бы постоянно.
        if (keyCode is LeftControl or RightControl or LeftShift or RightShift or
            LeftMenu or RightMenu or LeftWindows or RightWindows)
        {
            error = "Нужна клавиша-триггер, а не только модификатор: например, F9 или Ctrl+Shift+F9.";
            return false;
        }

        hotkey = new PushToTalkHotkey(keyCode, control, shift, alt, windows);
        return true;
    }

    private static bool TryParseModifier(string part, out Modifier modifier)
    {
        switch (part.Trim().ToLowerInvariant())
        {
            case "ctrl":
            case "control":
            case "контрол":
            case "ктрл":
                modifier = Modifier.Control;
                return true;
            case "shift":
            case "шифт":
                modifier = Modifier.Shift;
                return true;
            case "alt":
            case "альт":
                modifier = Modifier.Alt;
                return true;
            case "win":
            case "windows":
            case "вин":
                modifier = Modifier.Windows;
                return true;
            default:
                modifier = default;
                return false;
        }
    }

    /// <summary>
    /// Понимает имена клавиш, буквы, цифры и функциональные клавиши. Скан-коды
    /// намеренно не поддерживаются: в настройках это выглядело бы как число.
    /// </summary>
    private static bool TryParseKey(string part, out int keyCode)
    {
        var normalized = part.Trim().ToUpperInvariant();
        keyCode = 0;

        if (normalized.Length == 0)
        {
            return false;
        }

        // Клавиша без имени в списке (макроклавиша второй клавиатуры) сохраняется
        // как VK-код: захват в окне настроек записывает именно его.
        if (normalized.StartsWith("0X", StringComparison.Ordinal) &&
            normalized.Length is 3 or 4 &&
            int.TryParse(normalized[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexCode) &&
            hexCode is > 0 and <= 0xFF)
        {
            keyCode = hexCode;
            return true;
        }

        if (normalized.StartsWith('F') &&
            int.TryParse(normalized[1..], out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            keyCode = VkF1 + (functionNumber - 1);
            return true;
        }

        if (normalized.Length == 1)
        {
            var symbol = normalized[0];
            if (symbol is >= 'A' and <= 'Z')
            {
                keyCode = symbol;
                return true;
            }

            if (symbol is >= '0' and <= '9')
            {
                keyCode = symbol;
                return true;
            }
        }

        keyCode = normalized switch
        {
            "CAPS LOCK" or "CAPSLOCK" or "CAPS" or "КАПС" => CapsLockVirtualKey,
            "SPACE" or "ПРОБЕЛ" => 0x20,
            "TAB" or "ТАБ" => 0x09,
            "ENTER" or "ВВОД" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "BACKSPACE" => 0x08,
            "INSERT" or "INS" => 0x2D,
            "DELETE" or "DEL" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGE UP" or "PAGEUP" => 0x21,
            "PAGE DOWN" or "PAGEDOWN" => 0x22,
            "UP" or "ARROW UP" or "ВВЕРХ" => 0x26,
            "DOWN" or "ARROW DOWN" or "ВНИЗ" => 0x28,
            "LEFT" or "ARROW LEFT" or "ВЛЕВО" => 0x25,
            "RIGHT" or "ARROW RIGHT" or "ВПРАВО" => 0x27,
            "SCROLL LOCK" or "SCROLLLOCK" => 0x91,
            "PAUSE" or "BREAK" => 0x13,
            "`" or "~" or "TILDE" or "Ё" => 0xC0,
            "-" or "МИНУС" => 0xBD,
            "=" or "ПЛЮС" => 0xBB,
            "[" or "Х" => 0xDB,
            "]" or "Ъ" => 0xDD,
            ";" or "Ж" => 0xBA,
            "'" or "Э" => 0xDE,
            "," or "Б" => 0xBC,
            "." or "Ю" => 0xBE,
            "/" => 0xBF,
            "\\" => 0xDC,
            _ => 0
        };

        return keyCode != 0;
    }

    private static string DescribeKey(int keyCode) => keyCode switch
    {
        CapsLockVirtualKey => "Caps Lock",
        0x20 => "Space",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x08 => "Backspace",
        0x2D => "Insert",
        0x2E => "Delete",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x26 => "Up",
        0x28 => "Down",
        0x25 => "Left",
        0x27 => "Right",
        0x91 => "Scroll Lock",
        0x13 => "Pause",
        0xC0 => "`",
        0xBD => "-",
        0xBB => "=",
        0xDB => "[",
        0xDD => "]",
        0xBA => ";",
        0xDE => "'",
        0xBC => ",",
        0xBE => ".",
        0xBF => "/",
        0xDC => "\\",
        >= VkF1 and <= VkF24 => $"F{keyCode - VkF1 + 1}",
        >= 'A' and <= 'Z' => ((char)keyCode).ToString(),
        >= '0' and <= '9' => ((char)keyCode).ToString(),
        _ => $"0x{keyCode:X2}"
    };

    private const int VkF1 = 0x70;
    private const int VkF24 = 0x87;
    private const int LeftControl = 0xA2;
    private const int RightControl = 0xA3;
    private const int LeftShift = 0xA0;
    private const int RightShift = 0xA1;
    private const int LeftMenu = 0xA4;
    private const int RightMenu = 0xA5;
    private const int LeftWindows = 0x5B;
    private const int RightWindows = 0x5C;

    private enum Modifier
    {
        Control,
        Shift,
        Alt,
        Windows
    }
}
