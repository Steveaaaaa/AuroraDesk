using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AuroraDesk.Native;

public static class DesktopWindowPin
{
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_HWNDPARENT = -8;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOZORDER = 0x0004;

    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    /// <summary>
    /// Finds the WorkerW / Progman window that hosts SHELLDLL_DefView (the desktop icon layer).
    /// Setting GWLP_HWNDPARENT (owner, NOT child) to this handle makes Windows keep our layered
    /// widgets visible above the desktop even when Win+D / "Show Desktop" raises WorkerW.
    /// </summary>
    private static IntPtr FindDesktopHostWindow()
    {
        IntPtr progman = FindWindowW("Progman", null);
        if (progman != IntPtr.Zero &&
            FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
        {
            return progman;
        }

        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (FindWindowExW(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found != IntPtr.Zero ? found : progman;
    }

    /// <summary>
    /// Marks a window as a ToolWindow (hidden from Alt+Tab) and pins it at the bottom of the Z-order
    /// owned by the desktop host so it stays above the wallpaper and below all normal app windows,
    /// immune to Win+D and bottom-right "Show Desktop".
    /// </summary>
    public static void PinToDesktopLayer(Window window, bool noActivate = false)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        long exStyle = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_TOOLWINDOW;
        if (noActivate) exStyle |= WS_EX_NOACTIVATE;
        SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

        IntPtr desktopHost = FindDesktopHostWindow();
        if (desktopHost != IntPtr.Zero)
        {
            SetWindowLongPtr64(hwnd, GWLP_HWNDPARENT, desktopHost);
        }

        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(DesktopPinWndProc);
    }

    public static void MakeToolWindowOnly(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        long exStyle = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_TOOLWINDOW;
        SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    public static void SendToBottom(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    private static IntPtr DesktopPinWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SYSCOMMAND && (wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
        {
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            pos.hwndInsertAfter = HWND_BOTTOM;
            pos.flags &= ~SWP_NOZORDER;
            Marshal.StructureToPtr(pos, lParam, false);
        }

        return IntPtr.Zero;
    }
}
