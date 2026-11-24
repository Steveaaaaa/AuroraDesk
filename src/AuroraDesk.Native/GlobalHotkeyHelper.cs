using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AuroraDesk.Native;

public sealed class GlobalHotkeyHelper : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_SPACE = 0x20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private int _hotkeyId = 9001;
    private Action? _callback;

    public bool RegisterAltSpace(Window ownerWindow, Action onPressed)
    {
        _callback = onPressed;
        var hwnd = new WindowInteropHelper(ownerWindow).Handle;
        if (hwnd == IntPtr.Zero) return false;

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        return RegisterHotKey(hwnd, _hotkeyId, MOD_ALT | MOD_NOREPEAT, VK_SPACE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            _callback?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source != null && _source.Handle != IntPtr.Zero)
        {
            UnregisterHotKey(_source.Handle, _hotkeyId);
            _source.RemoveHook(WndProc);
        }
        _source = null;
    }
}
