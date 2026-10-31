using System.Runtime.InteropServices;

namespace ObsVoiceAssistant.Audio;

/// <summary>
/// Глобальный обработчик клавиши push-to-talk. Раньше это был только Caps Lock;
/// теперь клавиша или комбинация задаётся в настройках. Caps Lock остаётся
/// особым случаем: его обычное переключение не выполняется никогда, иначе
/// регистр букв менялся бы при каждой команде.
/// </summary>
public sealed class PushToTalkHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;

    private readonly PushToTalkController controller;
    private readonly Action<Exception>? errorHandler;
    private readonly HookProc callback;
    private nint hookHandle;

    // Отпускание триггера проверяется по этому флагу, а не по модификаторам:
    // пользователь может отпустить Ctrl раньше, чем F9, и запись всё равно
    // должна закончиться.
    private bool triggerIsActive;

    public PushToTalkHook(
        PushToTalkController controller,
        PushToTalkHotkey hotkey,
        Action<Exception>? errorHandler = null)
    {
        this.controller = controller;
        Hotkey = hotkey;
        this.errorHandler = errorHandler;
        callback = HandleKeyboardEvent;
    }

    public PushToTalkHotkey Hotkey { get; }

    public void Start()
    {
        if (hookHandle != 0)
        {
            return;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        hookHandle = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(module?.ModuleName), 0);
        if (hookHandle == 0)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (hookHandle == 0)
        {
            return;
        }

        UnhookWindowsHookEx(hookHandle);
        hookHandle = 0;
    }

    private nint HandleKeyboardEvent(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(hookHandle, code, wParam, lParam);
        }

        var message = (int)wParam;
        var virtualKey = Marshal.ReadInt32(lParam);
        var isKeyDown = message is WmKeydown or WmSyskeydown;
        var isKeyUp = message is WmKeyup or WmSyskeyup;

        if ((!isKeyDown && !isKeyUp) || virtualKey != Hotkey.VirtualKey)
        {
            return CallNextHookEx(hookHandle, code, wParam, lParam);
        }

        // Caps Lock как триггер перехватывается и никогда не переключает регистр.
        // Если же он назначен обычной клавишей, приложение его не трогает.
        if (Hotkey.VirtualKey == PushToTalkHotkey.CapsLockVirtualKey)
        {
            if (!ModifiersMatch())
            {
                return CallNextHookEx(hookHandle, code, wParam, lParam);
            }

            if (isKeyDown)
            {
                InvokeSafely(controller.KeyDown);
            }
            else
            {
                InvokeSafely(controller.KeyUp);
            }

            return 1;
        }

        if (isKeyUp)
        {
            // Запись заканчивается по отпусканию триггера, даже если модификаторы
            // уже отпущены: иначе Ctrl+Shift+F9 зависла бы навсегда.
            if (!triggerIsActive)
            {
                return CallNextHookEx(hookHandle, code, wParam, lParam);
            }

            triggerIsActive = false;
            InvokeSafely(controller.KeyUp);
            return 1;
        }

        if (triggerIsActive || !ModifiersMatch())
        {
            // Повтор при удержании и нажатие без нужных модификаторов не
            // запускают запись второй раз.
            return Hotkey.HasModifiers ? 1 : CallNextHookEx(hookHandle, code, wParam, lParam);
        }

        triggerIsActive = true;
        InvokeSafely(controller.KeyDown);

        // Комбинация не должна доходить до других приложений: Ctrl+Shift+F9
        // не останется «горячей клавишей» ещё где-то. Одиночную клавишу
        // пропускаем дальше, иначе она перестала бы работать в системе.
        return Hotkey.HasModifiers ? 1 : CallNextHookEx(hookHandle, code, wParam, lParam);
    }

    private bool ModifiersMatch()
    {
        var control = IsPressed(0x11);
        var shift = IsPressed(0x10);
        var alt = IsPressed(0x12);
        var windows = IsPressed(0x5B) || IsPressed(0x5C);

        return control == Hotkey.Control &&
               shift == Hotkey.Shift &&
               alt == Hotkey.Alt &&
               windows == Hotkey.Windows;
    }

    private static bool IsPressed(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private void InvokeSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            // Исключение из низкоуровневого хука не должно завершать процесс:
            // пользователь должен увидеть уведомление и повторить попытку.
            errorHandler?.Invoke(exception);
        }
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint moduleHandle, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hookHandle, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
}
