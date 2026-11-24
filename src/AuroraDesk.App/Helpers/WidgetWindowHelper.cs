using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AuroraDesk.Core.Models;
using AuroraDesk.Native;

namespace AuroraDesk.App.Helpers;

public static class WidgetWindowHelper
{
    public static void ConfigureDesktopWidget(Window win, WindowPos savedPos, Action onPositionSaved)
    {
        win.WindowStyle = WindowStyle.None;
        win.ResizeMode = ResizeMode.NoResize;
        win.ShowInTaskbar = false;
        win.AllowsTransparency = true;
        win.Background = System.Windows.Media.Brushes.Transparent;
        TextOptions.SetTextFormattingMode(win, TextFormattingMode.Display);
        RenderOptions.SetBitmapScalingMode(win, BitmapScalingMode.HighQuality);

        win.Left = savedPos.Left;
        win.Top = savedPos.Top;

        win.SourceInitialized += (_, _) =>
        {
            DesktopWindowPin.PinToDesktopLayer(win);
            ClampToWorkArea(win);
        };

        win.Loaded += (_, _) =>
        {
            DwmBlurHelper.EnableDesktopGlass(win, 16);
        };

        win.MouseLeftButtonUp += (_, _) =>
        {
            SnapToEdges(win);
            savedPos.Left = win.Left;
            savedPos.Top = win.Top;
            savedPos.HasCustomPosition = true;
            onPositionSaved();
        };
    }

    public static void EnableDragMove(Window win, MouseButtonEventArgs e, WindowPos savedPos, Action onPositionSaved)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            win.DragMove();
            SnapToEdges(win);
            savedPos.Left = win.Left;
            savedPos.Top = win.Top;
            savedPos.HasCustomPosition = true;
            onPositionSaved();
        }
        catch { }
    }

    public static void ClampToWorkArea(Window win)
    {
        var wa = SystemParameters.WorkArea;
        if (win.Left < wa.Left + 8) win.Left = wa.Left + 16;
        if (win.Top < wa.Top + 8) win.Top = wa.Top + 16;
        if (win.Left + win.Width > wa.Right - 8) win.Left = Math.Max(wa.Left + 16, wa.Right - win.Width - 24);
        if (win.Top + win.Height > wa.Bottom - 8) win.Top = Math.Max(wa.Top + 16, wa.Bottom - win.Height - 24);
    }

    private static void SnapToEdges(Window win)
    {
        var wa = SystemParameters.WorkArea;
        const double snapDist = 18;
        if (Math.Abs(win.Left - (wa.Left + 20)) < snapDist) win.Left = wa.Left + 20;
        if (Math.Abs((win.Left + win.Width) - (wa.Right - 24)) < snapDist) win.Left = wa.Right - win.Width - 24;
        if (Math.Abs(win.Top - (wa.Top + 28)) < snapDist) win.Top = wa.Top + 28;
    }

    public static SolidColorBrush CreateGlassTint(double opacity, string? tintHex = null)
    {
        byte alpha = (byte)Math.Clamp((int)(opacity * 255), 35, 225);
        System.Windows.Media.Color baseColor = System.Windows.Media.Color.FromRgb(14, 14, 24);
        if (!string.IsNullOrWhiteSpace(tintHex))
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(tintHex);
                baseColor = System.Windows.Media.Color.FromRgb(
                    (byte)(12 + c.R * 0.05),
                    (byte)(12 + c.G * 0.05),
                    (byte)(20 + c.B * 0.05));
            }
            catch { }
        }
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    public static SolidColorBrush ParseBrush(string hex, string fallback = "#A78BFA")
    {
        try
        {
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallback));
        }
    }
}
