using System.Windows;

namespace AuroraDesk.Native;

public static class DwmBlurHelper
{
    /// <summary>
    /// On Windows 11 24H2/25H2, legacy SetWindowCompositionAttribute(BLURBEHIND) on WS_EX_LAYERED
    /// windows turns alpha pixels opaque black, and DWMWA_SYSTEMBACKDROP_TYPE falls back to opaque
    /// grey whenever desktop widgets are unfocused. Pure WPF AllowsTransparency="True" with per-pixel
    /// alpha compositing guarantees crisp anti-aliased rounded corners and true translucency over
    /// Wallpaper Engine at all times.
    /// </summary>
    public static void EnableDesktopGlass(Window window, double cornerRadiusDip = 16)
    {
        // Intentionally left clean: per-pixel alpha transparency is handled natively by WPF's
        // AllowsTransparency="True" + Background="Transparent" without DWM opaque fallbacks.
    }
}
