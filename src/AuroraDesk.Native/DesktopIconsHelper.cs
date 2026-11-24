using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AuroraDesk.Native;

public static class DesktopIconsHelper
{
    private const int WM_COMMAND = 0x0111;
    private const int ID_TOGGLE_DESKTOP_ICONS = 0x7402;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private static (IntPtr defView, IntPtr listView) FindDesktopListView()
    {
        IntPtr foundDefView = IntPtr.Zero;
        IntPtr foundListView = IntPtr.Zero;

        EnumWindows((topHwnd, _) =>
        {
            EnumChildWindows(topHwnd, (child, __) =>
            {
                var sb = new StringBuilder(256);
                GetClassName(child, sb, sb.Capacity);
                string cls = sb.ToString();
                if (cls == "SHELLDLL_DefView") foundDefView = child;
                else if (cls == "SysListView32" && foundDefView != IntPtr.Zero)
                {
                    foundListView = child;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return foundListView == IntPtr.Zero;
        }, IntPtr.Zero);

        return (foundDefView, foundListView);
    }

    public static bool AreDesktopIconsVisible()
    {
        var (_, listView) = FindDesktopListView();
        return listView != IntPtr.Zero && IsWindowVisible(listView);
    }

    public static void SetDesktopIconsVisible(bool visible)
    {
        var (defView, listView) = FindDesktopListView();
        if (defView != IntPtr.Zero && listView != IntPtr.Zero)
        {
            bool current = IsWindowVisible(listView);
            if (current != visible)
            {
                SendMessage(defView, WM_COMMAND, new IntPtr(ID_TOGGLE_DESKTOP_ICONS), IntPtr.Zero);
            }
        }

        try
        {
            using var adv = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            adv?.SetValue("HideIcons", visible ? 0 : 1, RegistryValueKind.DWord);
        }
        catch { }
    }

    public static void SetSystemIconsHidden(bool hidden)
    {
        string[] paths =
        [
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu"
        ];
        string[] guids =
        [
            "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", // This PC
            "{645FF040-5081-101B-9F08-00AA002F954E}", // Recycle Bin
            "{2cc5ca98-6485-489a-920e-b3e88a6ccce3}"  // Spotlight info icon
        ];

        foreach (var p in paths)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(p);
                if (key == null) continue;
                foreach (var g in guids)
                {
                    if (hidden) key.SetValue(g, 1, RegistryValueKind.DWord);
                    else key.DeleteValue(g, false);
                }
            }
            catch { }
        }
    }
}
