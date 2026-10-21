using System.Runtime.InteropServices;

namespace ObsVoiceAssistant.Audio;

public sealed class CapsLockHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int VkCapital = 0x14;

    private readonly PushToTalkController controller;
    private readonly Action<Exception>? errorHandler;
    private readonly HookProc callback;
    private nint hookHandle;

    public CapsLockHook(PushToTalkController controller, Action<Exception>? errorHandler = null)
    {
        this.controller = controller;
        this.errorHandler = errorHandler;
        callback = HandleKeyboardEvent;
    }

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
        if (code >= 0 && Marshal.ReadInt32(lParam) == VkCapital)
        {
            if (wParam == WmKeydown)
            {
                InvokeSafely(controller.KeyDown);
            }
            else if (wParam == WmKeyup)
            {
                InvokeSafely(controller.KeyUp);
            }

            return 1;
        }

        return CallNextHookEx(hookHandle, code, wParam, lParam);
    }

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);
}
