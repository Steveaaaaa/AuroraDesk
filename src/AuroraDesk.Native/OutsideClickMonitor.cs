using System;
using System.Runtime.InteropServices;

namespace AuroraDesk.Native;

public sealed class OutsideClickMonitor
{
    private const int LowLevelMouseHook = 14;
    private const int LeftButtonDown = 0x0201;
    private const int RightButtonDown = 0x0204;
    private const int MiddleButtonDown = 0x0207;
    private const int ExtraButtonDown = 0x020B;
    private readonly HookProc _callback;
    private readonly Action _outsideClick;
    private IntPtr _hook;

    public OutsideClickMonitor(Action outsideClick)
    {
        _outsideClick = outsideClick;
        _callback = OnMouseEvent;
    }

    public void Start()
    {
        if (_hook == IntPtr.Zero)
            _hook = SetWindowsHookEx(LowLevelMouseHook, _callback, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnMouseEvent(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() is LeftButtonDown or RightButtonDown or MiddleButtonDown or ExtraButtonDown)
        {
            var point = Marshal.PtrToStructure<ScreenPoint>(data);
            IntPtr window = WindowFromPoint(point);
            GetWindowThreadProcessId(window, out uint processId);
            if (processId != (uint)Environment.ProcessId) _outsideClick();
        }

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
