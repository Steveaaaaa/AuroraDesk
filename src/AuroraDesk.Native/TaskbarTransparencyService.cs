using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AuroraDesk.Native;

public enum TaskbarVisualMode
{
    Clear,
    Translucent,
    Acrylic
}

/// <summary>
/// Dual-engine taskbar transparency controller:
/// - Win11 22H2–25H2: Mounts AuroraDesk.TaskbarTap.dll via InitializeXamlDiagnosticsEx to hide the XAML
///   TaskbarFrame BackgroundFill &amp; BackgroundStroke cleanly on the UI thread, paired with DWM composition.
/// - Win10 / Win11 21H2: Falls back seamlessly to SetWindowCompositionAttribute on Shell_TrayWnd.
/// Restores the native taskbar on disable or exit.
/// </summary>
public sealed class TaskbarTransparencyService : IDisposable
{
    // {7A4E2F19-8C3B-4D6A-9E12-5B7F8C9D0E31}
    private static readonly Guid ClsidAuroraTaskbarTap = new("7a4e2f19-8c3b-4d6a-9e12-5b7f8c9d0e31");
    private const uint TapMagic = 0x41555241u; // 'AURA'
    private const int DwmwaCloaked = 14;
    private const int TapHeartbeatMs = 1000;
    private const int TapRetryMs = 3000;

    private readonly object _sync = new();
    private Timer? _timer;
    private NamedPipeServerStream? _pipe;
    private uint _explorerPid;
    private IntPtr _primaryTaskbar;
    private long _nextAttachMs;
    private long _lastTapWriteMs;
    private bool _attachInProgress;
    private int _generation;
    private int _configRevision;
    private bool _compositionApplied;
    private bool _enabled;
    private TaskbarVisualMode _mode = TaskbarVisualMode.Clear;
    private bool _darkOnMaximize = true;
    private float _lastSentFill = -1f;
    private float _lastSentStroke = -1f;
    private bool _hasSampledColor;
    private float _curR = 235f, _curG = 238f, _curB = 245f, _curA = 24f;
    private float _targetR = 235f, _targetG = 238f, _targetB = 245f, _targetA = 124f;
    private long _lastSampleTickMs;
    private long _lastFrameTickMs;
    private long _lastKeepAliveMs;
    private IntPtr _lastForegroundHwnd;
    private long _foregroundChangedMs;
    private uint _lastAppliedAbgr = uint.MaxValue;
    private byte _lastAppliedR, _lastAppliedG, _lastAppliedB, _lastAppliedA;
    private AccentState _lastAppliedAccent = (AccentState)(-1);
    private bool _inFastAnimation;
    private int _tickBusy;
    private bool _disposed;

    public bool IsTapConnected
    {
        get { lock (_sync) return _pipe?.IsConnected == true; }
    }

    public void Configure(bool enabled, string modeName, bool darkOnMaximize)
    {
        var parsed = modeName?.Trim().ToLowerInvariant() switch
        {
            "translucent" => TaskbarVisualMode.Translucent,
            "acrylic" => TaskbarVisualMode.Acrylic,
            _ => TaskbarVisualMode.Clear
        };

        lock (_sync)
        {
            if (_disposed || (_enabled == enabled && _mode == parsed && _darkOnMaximize == darkOnMaximize)) return;

            _mode = parsed;
            _darkOnMaximize = darkOnMaximize;
            _enabled = enabled;
            _configRevision++;

            if (!_enabled)
            {
                _generation++;
                StopTimerLocked();
                DetachTapLocked(restoreNative: true);
                _explorerPid = 0;
                _primaryTaskbar = IntPtr.Zero;
                RestoreCompositionLocked();
                return;
            }

            _inFastAnimation = false;
            _lastAppliedAccent = (AccentState)(-1);
            _timer ??= new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(0, 250);
        }
    }

    private void Tick()
    {
        if (Interlocked.CompareExchange(ref _tickBusy, 1, 0) != 0) return;
        try
        {
            TickCore();
        }
        finally
        {
            Volatile.Write(ref _tickBusy, 0);
        }
    }

    private void TickCore()
    {
        IntPtr primaryTaskbar = FindWindowW("Shell_TrayWnd", null);
        if (primaryTaskbar == IntPtr.Zero) return;

        GetWindowThreadProcessId(primaryTaskbar, out uint explorerPid);
        if (explorerPid == 0) return;

        long now = Environment.TickCount64;
        TaskbarVisualMode mode;
        bool darkOnMax;
        int revision;
        lock (_sync)
        {
            if (!_enabled || _disposed) return;
            if (explorerPid != _explorerPid)
            {
                _generation++;
                DetachTapLocked(restoreNative: false);
                _explorerPid = explorerPid;
                _nextAttachMs = now;
            }
            if (_primaryTaskbar != primaryTaskbar)
            {
                _primaryTaskbar = primaryTaskbar;
                _lastAppliedAccent = (AccentState)(-1);
            }
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) &&
                !_attachInProgress && _pipe == null && now >= _nextAttachMs)
            {
                StartTapAttachLocked(explorerPid, _generation);
            }

            mode = _mode;
            darkOnMax = _darkOnMaximize;
            revision = _configRevision;
        }

        IntPtr topMaxHwnd = IntPtr.Zero;
        bool isMaximized = darkOnMax && HasVisibleMaximizedWindowAboveDesktop(out topMaxHwnd);

        // Compute desired composition & XAML opacities
        AccentState accent;
        uint abgrColor;
        float fillOpacity;
        float strokeOpacity;

        float dtSec = _lastFrameTickMs > 0
            ? Math.Clamp((now - _lastFrameTickMs) / 1000f, 0.016f, 0.12f)
            : 0.04f;
        _lastFrameTickMs = now;

        // Track active maximized window switches so we never stall DWM with BitBlt mid-animation
        if (topMaxHwnd != _lastForegroundHwnd)
        {
            _lastForegroundHwnd = topMaxHwnd;
            _foregroundChangedMs = now;
        }

        bool needFastFrames = false;

        if (isMaximized)
        {
            bool windowSettled = (now - _foregroundChangedMs) >= 240;
            bool sampleDue = !_hasSampledColor
                ? windowSettled
                : (windowSettled && (now - _lastSampleTickMs >= 900 || _lastSampleTickMs < _foregroundChangedMs));

            if (sampleDue)
            {
                _lastSampleTickMs = now;
                if (TrySampleStripAboveTaskbar(primaryTaskbar, out byte sr, out byte sg, out byte sb))
                {
                    double lum = 0.299 * sr + 0.587 * sg + 0.114 * sb;
                    float targetA = lum >= 145.0 ? 122f : (lum < 95.0 ? 138f : 130f);

                    if (!_hasSampledColor)
                    {
                        _curR = sr;
                        _curG = sg;
                        _curB = sb;
                        _curA = 28f;
                        _targetR = sr;
                        _targetG = sg;
                        _targetB = sb;
                        _targetA = targetA;
                        _hasSampledColor = true;
                    }
                    else
                    {
                        float diff = MathF.Abs(sr - _targetR) + MathF.Abs(sg - _targetG) + MathF.Abs(sb - _targetB);
                        if (diff >= 12f || MathF.Abs(targetA - _targetA) >= 4f)
                        {
                            _targetR = sr;
                            _targetG = sg;
                            _targetB = sb;
                            _targetA = targetA;
                        }
                    }
                }
            }

            // Gentle, zero-stutter exponential glide (tau = 0.38s at 25fps -> ~0.9s smooth transition)
            float alphaStep = 1f - MathF.Exp(-dtSec / 0.38f);
            _curR += (_targetR - _curR) * alphaStep;
            _curG += (_targetG - _curG) * alphaStep;
            _curB += (_targetB - _curB) * alphaStep;
            _curA += (_targetA - _curA) * alphaStep;

            float remaining =
                MathF.Abs(_targetR - _curR) +
                MathF.Abs(_targetG - _curG) +
                MathF.Abs(_targetB - _curB) +
                MathF.Abs(_targetA - _curA);

            if (remaining > 2.0f)
            {
                needFastFrames = true;
            }
            else
            {
                _curR = _targetR;
                _curG = _targetG;
                _curB = _targetB;
                _curA = _targetA;
            }

            byte r = (byte)Math.Clamp((int)MathF.Round(_curR), 0, 255);
            byte g = (byte)Math.Clamp((int)MathF.Round(_curG), 0, 255);
            byte b = (byte)Math.Clamp((int)MathF.Round(_curB), 0, 255);
            byte a = (byte)Math.Clamp((int)MathF.Round(_curA), 1, 255);

            accent = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
            abgrColor = ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
            fillOpacity = 0.0f;
            strokeOpacity = 0.22f;
        }
        else
        {
            _hasSampledColor = false;
            switch (mode)
            {
                case TaskbarVisualMode.Translucent:
                    accent = AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT;
                    abgrColor = 0x7A1E1410; // ~48% dark translucent
                    fillOpacity = 0.0f;
                    strokeOpacity = 0.16f;
                    break;
                case TaskbarVisualMode.Acrylic:
                    accent = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                    abgrColor = 0x551E1410; // subtle dark acrylic blur
                    fillOpacity = 0.0f;
                    strokeOpacity = 0.0f;
                    break;
                default: // Clear
                    accent = AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT;
                    abgrColor = 0x00000000; // 100% transparent
                    fillOpacity = 0.0f;
                    strokeOpacity = 0.0f;
                    break;
            }
        }

        // Switch timer dynamically: 40ms (~25fps, zero DWM backlog) during glide, 250ms when settled (~0% CPU)
        lock (_sync)
        {
            if (!_enabled || _disposed || revision != _configRevision) return;
            if (_timer != null && needFastFrames != _inFastAnimation)
            {
                _inFastAnimation = needFastFrames;
                int interval = needFastFrames ? 40 : 250;
                _timer.Change(interval, interval);
            }

            byte nextR = (byte)(abgrColor & 0xFF);
            byte nextG = (byte)((abgrColor >> 8) & 0xFF);
            byte nextB = (byte)((abgrColor >> 16) & 0xFF);
            byte nextA = (byte)((abgrColor >> 24) & 0xFF);
            int channelDelta = Math.Abs(nextR - _lastAppliedR) + Math.Abs(nextG - _lastAppliedG) +
                Math.Abs(nextB - _lastAppliedB) + Math.Abs(nextA - _lastAppliedA);

            if (accent != _lastAppliedAccent ||
                (abgrColor != _lastAppliedAbgr && (!needFastFrames || channelDelta >= 3)) ||
                now - _lastKeepAliveMs >= 800)
            {
                _lastAppliedAccent = accent;
                _lastAppliedAbgr = abgrColor;
                _lastAppliedR = nextR;
                _lastAppliedG = nextG;
                _lastAppliedB = nextB;
                _lastAppliedA = nextA;
                _lastKeepAliveMs = now;
                foreach (IntPtr hwnd in GetAllTaskbarWindows())
                    _compositionApplied |= SetTaskbarComposition(hwnd, accent, abgrColor);
            }

            SendTapOpacityLocked(fillOpacity, strokeOpacity, now);
        }
    }

    private void StartTapAttachLocked(uint explorerPid, int generation)
    {
        _nextAttachMs = Environment.TickCount64 + TapRetryMs;
        string tapDllPath = Path.Combine(AppContext.BaseDirectory, "AuroraDesk.TaskbarTap.dll");
        if (!File.Exists(tapDllPath)) return;
        _attachInProgress = true;

        _ = Task.Run(async () =>
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                string pipeShortName = $"AuroraDesk_TaskbarTap_{Environment.ProcessId}_{Guid.NewGuid():N}";
                string pipeFullName = $@"\\.\pipe\{pipeShortName}";

                pipe = new NamedPipeServerStream(
                    pipeShortName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                Guid clsid = ClsidAuroraTaskbarTap;
                int hr = -1;
                for (int endpoint = 1; endpoint <= 32; endpoint++)
                {
                    hr = InitializeXamlDiagnosticsEx(
                        $"VisualDiagConnection{endpoint}",
                        explorerPid,
                        "",
                        tapDllPath,
                        ref clsid,
                        pipeFullName);
                    if (hr is not (unchecked((int)0x800700B7) or unchecked((int)0x80070490))) break;
                }

                if (hr >= 0)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
                    lock (_sync)
                    {
                        if (_enabled && !_disposed && generation == _generation)
                        {
                            _pipe = pipe;
                            pipe = null;
                            _lastAppliedAccent = (AccentState)(-1);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            finally
            {
                pipe?.Dispose();
                lock (_sync)
                {
                    _attachInProgress = false;
                }
            }
        });
    }

    private void SendTapOpacityLocked(float fillOpacity, float strokeOpacity, long now)
    {
        if (_pipe == null) return;
        if (!_pipe.IsConnected)
        {
            DetachTapLocked(restoreNative: false);
            return;
        }
        if (now - _lastTapWriteMs < TapHeartbeatMs &&
            Math.Abs(_lastSentFill - fillOpacity) < 0.001f &&
            Math.Abs(_lastSentStroke - strokeOpacity) < 0.001f) return;

        if (WriteTapCommandLocked(1u, fillOpacity, strokeOpacity))
        {
            _lastTapWriteMs = now;
            _lastSentFill = fillOpacity;
            _lastSentStroke = strokeOpacity;
        }
        else
        {
            DetachTapLocked(restoreNative: false);
        }
    }

    private bool WriteTapCommandLocked(uint cmd, float fillOpacity, float strokeOpacity)
    {
        if (_pipe == null || !_pipe.IsConnected) return false;
        try
        {
            byte[] buf = new byte[16];
            BitConverter.TryWriteBytes(buf.AsSpan(0, 4), TapMagic);
            BitConverter.TryWriteBytes(buf.AsSpan(4, 4), cmd);
            BitConverter.TryWriteBytes(buf.AsSpan(8, 4), fillOpacity);
            BitConverter.TryWriteBytes(buf.AsSpan(12, 4), strokeOpacity);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            _pipe.WriteAsync(buf.AsMemory(), timeout.Token).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (IOException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (OperationCanceledException) { return false; }
    }

    private void DetachTapLocked(bool restoreNative)
    {
        if (_pipe != null)
        {
            if (restoreNative && _pipe.IsConnected)
            {
                WriteTapCommandLocked(2u, 1.0f, 1.0f);
            }
            try { _pipe.Dispose(); } catch { }
            _pipe = null;
        }
        _lastTapWriteMs = 0;
        _lastSentFill = -1f;
        _lastSentStroke = -1f;
    }

    private void StopTimerLocked()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static bool TrySampleStripAboveTaskbar(IntPtr taskbarHwnd, out byte avgR, out byte avgG, out byte avgB)
    {
        avgR = 235;
        avgG = 238;
        avgB = 245;

        if (!GetWindowRect(taskbarHwnd, out RECT tbRect)) return false;
        int width = tbRect.Right - tbRect.Left;
        int sampleY = tbRect.Top - 12;
        if (width < 64 || sampleY < 0) return false;

        int stripX = tbRect.Left + 40;
        int stripW = Math.Max(32, width - 80);

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;

        IntPtr memDc = IntPtr.Zero;
        IntPtr dib = IntPtr.Zero;
        IntPtr oldBmp = IntPtr.Zero;

        try
        {
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return false;

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = stripW,
                    biHeight = -1, // top-down 1-pixel strip
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0 // BI_RGB
                }
            };

            dib = CreateDIBSection(memDc, ref bmi, 0, out IntPtr bitsPtr, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bitsPtr == IntPtr.Zero) return false;

            oldBmp = SelectObject(memDc, dib);
            if (!BitBlt(memDc, 0, 0, stripW, 1, screenDc, stripX, sampleY, 0x00CC0020 /* SRCCOPY */))
            {
                return false;
            }

            const int samples = 32;
            long sumR = 0, sumG = 0, sumB = 0;
            for (int i = 0; i < samples; i++)
            {
                int px = (int)((long)i * (stripW - 1) / (samples - 1));
                int bgra = Marshal.ReadInt32(bitsPtr, px * 4);
                sumB += bgra & 0xFF;
                sumG += (bgra >> 8) & 0xFF;
                sumR += (bgra >> 16) & 0xFF;
            }

            avgR = (byte)(sumR / samples);
            avgG = (byte)(sumG / samples);
            avgB = (byte)(sumB / samples);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (oldBmp != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldBmp);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Walks top-level windows in Z-order from top to bottom.
    /// Returns true if a visible, non-cloaked maximized app window sits above the desktop layer
    /// (immune to clicking the taskbar or Alt+Tab, yet immediately detects Win+D Show Desktop).
    /// </summary>
    private static bool HasVisibleMaximizedWindowAboveDesktop(out IntPtr topMaximizedHwnd)
    {
        topMaximizedHwnd = IntPtr.Zero;
        var sb = new StringBuilder(64);
        int selfPid = Environment.ProcessId;

        IntPtr hwnd = GetTopWindow(IntPtr.Zero);
        while (hwnd != IntPtr.Zero)
        {
            if (IsWindowVisible(hwnd))
            {
                sb.Clear();
                GetClassNameW(hwnd, sb, sb.Capacity);
                string cls = sb.ToString();

                // Hit the desktop layer first (e.g. after Win+D Show Desktop) -> no maximized window visible
                if (cls == "Progman" ||
                    (cls == "WorkerW" && FindWindowExW(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero))
                {
                    return false;
                }

                if (cls is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow"
                    or "XamlExplorerHostIslandWindow" or "TopLevelWindowForOverflowXamlIsland"))
                {
                    if (IsZoomed(hwnd) && !IsIconic(hwnd))
                    {
                        GetWindowThreadProcessId(hwnd, out uint pid);
                        if (pid != (uint)selfPid)
                        {
                            bool cloaked = DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int c, sizeof(int)) == 0 && c != 0;
                            if (!cloaked)
                            {
                                topMaximizedHwnd = hwnd;
                                return true;
                            }
                        }
                    }
                }
            }

            hwnd = GetWindow(hwnd, 2 /* GW_HWNDNEXT */);
        }

        return false;
    }

    private static List<IntPtr> GetAllTaskbarWindows()
    {
        var list = new List<IntPtr>(2);
        IntPtr primary = FindWindowW("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) list.Add(primary);

        IntPtr secondary = IntPtr.Zero;
        while (true)
        {
            secondary = FindWindowExW(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null);
            if (secondary == IntPtr.Zero) break;
            list.Add(secondary);
        }
        return list;
    }

    private void RestoreCompositionLocked()
    {
        if (!_compositionApplied) return;
        foreach (IntPtr hwnd in GetAllTaskbarWindows())
        {
            SetTaskbarComposition(hwnd, AccentState.ACCENT_DISABLED, 0);
        }
        _compositionApplied = false;
    }

    private static bool SetTaskbarComposition(IntPtr hwnd, AccentState state, uint abgrColor)
    {
        if (hwnd == IntPtr.Zero) return false;
        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == AccentState.ACCENT_DISABLED ? 0 : 2,
            GradientColor = abgrColor,
            AnimationId = 0
        };

        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = 19, // WCA_ACCENT_POLICY
                Data = ptr,
                SizeOfData = (nuint)size
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;
            StopTimerLocked();
            DetachTapLocked(restoreNative: true);
            RestoreCompositionLocked();
        }
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public nuint SizeOfData;
    }

    [DllImport("Windows.UI.Xaml.dll", CharSet = CharSet.Unicode)]
    private static extern int InitializeXamlDiagnosticsEx(
        string endPointName,
        uint pid,
        string wszDllXamlDiagnostics,
        string wszTAPDllName,
        ref Guid tapClsid,
        string wszInitializationData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
}
