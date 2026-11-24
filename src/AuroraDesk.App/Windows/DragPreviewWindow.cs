using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace AuroraDesk.App.Windows;

/// <summary>
/// Click-through floating drag ghost pill that follows the cursor smoothly during DragDrop.DoDragDrop.
/// </summary>
public sealed class DragPreviewWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private readonly ScaleTransform _scale = new(0.86, 0.86);

    public DragPreviewWindow(BitmapSource? icon, string title)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        var stack = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (icon != null)
        {
            stack.Children.Add(new System.Windows.Controls.Image
            {
                Source = icon,
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0)
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            MaxWidth = 170,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        var pill = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(205, 24, 24, 42)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(190, 196, 181, 253)),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 7, 13, 7),
            Margin = new Thickness(8),
            Opacity = 0.94,
            Child = stack,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = _scale,
            Effect = new DropShadowEffect
            {
                Color = System.Windows.Media.Color.FromRgb(10, 10, 20),
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.55
            }
        };

        Content = pill;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                long exStyle = GetWindowLongPtr64(hwnd, GWL_EXSTYLE).ToInt64();
                exStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
            }
        };

        Loaded += (_, _) =>
        {
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1.0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1.0, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        };

        UpdatePositionToCursor();
    }

    public void UpdatePositionToCursor()
    {
        if (!GetCursorPos(out var pt)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        Left = pt.X / scaleX + 10;
        Top = pt.Y / scaleY + 8;
    }
}
