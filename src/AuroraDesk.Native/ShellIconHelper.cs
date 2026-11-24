using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AuroraDesk.Native;

public static class ShellIconHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_ICONONLY = 0x04
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        [In] IntPtr pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFO lpbmi, uint uUsage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// Extracts a 32-bit premultiplied ARGB icon directly into a frozen WPF BitmapSource
    /// without passing through GDI+ Image.FromHbitmap (which strips alpha channels).
    /// </summary>
    public static BitmapSource? ExtractIcon(string shellOrFilePath, int size = 48)
    {
        try
        {
            SHCreateItemFromParsingName(shellOrFilePath, IntPtr.Zero, IID_IShellItemImageFactory, out var factory);
            if (factory == null) return null;

            try
            {
                var sz = new SIZE { cx = size, cy = size };
                int hr = factory.GetImage(sz, SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK, out var hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                {
                    hr = factory.GetImage(sz, SIIGBF.SIIGBF_RESIZETOFIT, out hBitmap);
                }
                if (hr != 0 || hBitmap == IntPtr.Zero) return null;

                try
                {
                    if (GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
                    int w = bm.bmWidth;
                    int h = Math.Abs(bm.bmHeight);
                    if (w <= 0 || h <= 0) return null;

                    var bmi = new BITMAPINFO();
                    bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                    bmi.bmiHeader.biWidth = w;
                    bmi.bmiHeader.biHeight = -h; // top-down DIB
                    bmi.bmiHeader.biPlanes = 1;
                    bmi.bmiHeader.biBitCount = 32;
                    bmi.bmiHeader.biCompression = 0;

                    byte[] pixels = new byte[w * h * 4];
                    IntPtr hdc = GetDC(IntPtr.Zero);
                    try
                    {
                        if (GetDIBits(hdc, hBitmap, 0, (uint)h, pixels, ref bmi, 0) == 0) return null;
                    }
                    finally
                    {
                        ReleaseDC(IntPtr.Zero, hdc);
                    }

                    bool hasAlpha = false;
                    for (int i = 3; i < pixels.Length; i += 4)
                    {
                        if (pixels[i] != 0) { hasAlpha = true; break; }
                    }
                    if (!hasAlpha)
                    {
                        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                    }

                    var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                    wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
                    wb.Freeze();
                    return wb;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return null;
        }
    }

    public static string? GetOrCacheIconPng(string targetPath, string cacheDir, string? fixedName = null, int size = 48)
    {
        if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

        string fileName;
        if (!string.IsNullOrEmpty(fixedName))
        {
            fileName = fixedName;
        }
        else
        {
            byte[] bytes = MD5.HashData(Encoding.UTF8.GetBytes(targetPath.ToLowerInvariant()));
            fileName = "app_" + Convert.ToHexString(bytes)[..12] + ".png";
        }

        string pngPath = Path.Combine(cacheDir, fileName);
        if (File.Exists(pngPath) && new FileInfo(pngPath).Length > 100) return pngPath;

        var bmp = ExtractIcon(targetPath, size);
        if (bmp == null) return null;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = new FileStream(pngPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(fs);
        return pngPath;
    }

    public static BitmapSource? LoadCachedPng(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }
}
