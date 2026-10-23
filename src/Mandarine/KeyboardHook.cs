using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mandarine;

/// <summary>Global low-level keyboard hook. Raises ShiftTapped / CtrlTapped / AltTapped when the key goes down, Escape on Esc.</summary>
public sealed class KeyboardHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    const int VK_SHIFT = 0x10, VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_CONTROL = 0x11, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_MENU = 0x12, VK_LMENU = 0xA4, VK_RMENU = 0xA5, VK_ESCAPE = 0x1B, VK_LBUTTON = 0x01;

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);

    readonly HookProc _proc; // keep alive: the OS holds only a raw pointer
    readonly IntPtr _hook;
    bool _shiftDown, _ctrlDown, _altDown;

    public event Action? ShiftTapped;
    public event Action? CtrlTapped;
    public event Action? AltTapped;
    public event Action? Escape;

    public static bool LeftButtonDown => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    public KeyboardHook()
    {
        _proc = Callback;
        using var module = Process.GetCurrentProcess().MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
        if (_hook == IntPtr.Zero) throw new InvalidOperationException($"SetWindowsHookEx falló ({Marshal.GetLastWin32Error()}).");
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var vk = Marshal.ReadInt32(lParam);
            var msg = (int)wParam;
            var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            var up = msg is WM_KEYUP or WM_SYSKEYUP;
            if (vk is VK_SHIFT or VK_LSHIFT or VK_RSHIFT)
            {
                if (down && !_shiftDown) ShiftTapped?.Invoke(); // ignore auto-repeat
                if (down) _shiftDown = true;
                if (up) _shiftDown = false;
            }
            else if (vk is VK_CONTROL or VK_LCONTROL or VK_RCONTROL)
            {
                if (down && !_ctrlDown) CtrlTapped?.Invoke();
                if (down) _ctrlDown = true;
                if (up) _ctrlDown = false;
            }
            else if (vk is VK_MENU or VK_LMENU or VK_RMENU)
            {
                if (down && !_altDown) AltTapped?.Invoke();
                if (down) _altDown = true;
                if (up) _altDown = false;
            }
            else if (vk == VK_ESCAPE && down) Escape?.Invoke();
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => UnhookWindowsHookEx(_hook);
}
