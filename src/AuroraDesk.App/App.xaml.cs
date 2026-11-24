using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Animation;
using AuroraDesk.App.Helpers;
using AuroraDesk.App.Windows;
using AuroraDesk.Native;

namespace AuroraDesk.App;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private AppController? _controller;
    private LeftSystemWindow? _leftSystem;
    private LeftRecentWindow? _leftRecent;
    private LeftMostWindow? _leftMost;
    private RightHubWindow? _rightHub;
    private DrawerWindow? _drawer;
    private SearchWindow? _search;
    private SettingsWindow? _settings;
    private NotifyIcon? _trayIcon;
    private GlobalHotkeyHelper? _hotkey;
    private TaskbarTransparencyService? _taskbarService;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private void GlassCard_Press(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation || sender is not System.Windows.Controls.Border card) return;
        if (card.RenderTransform is not System.Windows.Media.ScaleTransform scale || scale.IsFrozen)
        {
            scale = new System.Windows.Media.ScaleTransform(1, 1);
            card.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            card.RenderTransform = scale;
        }
        AnimateCardScale(scale, 0.985, 65);
    }

    private void GlassCard_Release(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Border { RenderTransform: System.Windows.Media.ScaleTransform scale } && !scale.IsFrozen)
            AnimateCardScale(scale, 1, 115);
    }

    private static void AnimateCardScale(System.Windows.Media.ScaleTransform scale, double target, int milliseconds)
    {
        var animation = new DoubleAnimation(scale.ScaleX, target, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        scale.ScaleX = target;
        scale.ScaleY = target;
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, animation);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(true, "AuroraDesk_SingleInstance_Mutex", out _ownsInstanceMutex);
        if (!_ownsInstanceMutex)
        {
            System.Windows.MessageBox.Show("AuroraDesk 已经在后台运行，请点击右下角托盘图标或按 Alt + Space 打开搜索！", "AuroraDesk");
            Shutdown();
            return;
        }

        _controller = new AppController();

        if (!_controller.Config.Initialized)
        {
            var wizard = new WelcomeWizardWindow(_controller);
            bool? ok = wizard.ShowDialog();
            if (ok != true)
            {
                _controller.Config.Initialized = true;
                _controller.ConfigService.Save();
            }
        }

        _leftSystem = new LeftSystemWindow(_controller);
        _leftRecent = new LeftRecentWindow(_controller);
        _leftMost = new LeftMostWindow(_controller);
        _rightHub = new RightHubWindow(_controller);
        _drawer = new DrawerWindow(_controller);
        _ = new StorageOpacityController(_controller, _rightHub.RootBorder, _drawer.RootBorder);
        _search = new SearchWindow(_controller);

        _controller.OpenDrawerRequested += (folder, title, toggle) =>
        {
            if (_drawer != null && _rightHub != null)
            {
                _drawer.OpenFolder(folder, title, _rightHub, toggle);
            }
        };
        _controller.CloseDrawerIfMatchesRequested += folder => _drawer?.CloseIfShowingFolder(folder);
        _controller.OpenSearchRequested += () => _search?.OpenAndFocus();
        _controller.OpenSettingsRequested += OpenSettingsWindow;

        _leftSystem.Show();
        if (_controller.Config.ShowRecentWidget) _leftRecent.Show();
        if (_controller.Config.ShowMostWidget) _leftMost.Show();
        _rightHub.Show();

        // Register Alt + Space global hotkey on RightHub window handle
        _hotkey = new GlobalHotkeyHelper();
        _hotkey.RegisterAltSpace(_rightHub, () => Dispatcher.Invoke(() => _search?.OpenAndFocus()));

        DesktopIconsHelper.SetSystemIconsHidden(_controller.Config.HideDesktopIcons);
        DesktopIconsHelper.SetDesktopIconsVisible(!_controller.Config.HideDesktopIcons);

        _controller.ApplyAutostart(_controller.Config.AutoStart);
        _controller.UpdateDesktopWatcher();
        _taskbarService = new TaskbarTransparencyService();
        ApplyTaskbarTransparency();
        _controller.ConfigChanged += ApplyTaskbarTransparency;
        _controller.ConfigChanged += ApplyWidgetVisibility;
        SetupTrayIcon();
    }

    private void ApplyWidgetVisibility()
    {
        if (_controller == null) return;
        Dispatcher.Invoke(() =>
        {
            if (_leftRecent != null)
            {
                if (_controller.Config.ShowRecentWidget) { if (!_leftRecent.IsVisible) _leftRecent.Show(); }
                else { if (_leftRecent.IsVisible) _leftRecent.Hide(); }
            }
            if (_leftMost != null)
            {
                if (_controller.Config.ShowMostWidget) { if (!_leftMost.IsVisible) _leftMost.Show(); }
                else { if (_leftMost.IsVisible) _leftMost.Hide(); }
            }
        });
    }

    private void ApplyTaskbarTransparency()
    {
        if (_controller == null || _taskbarService == null) return;
        _taskbarService.Configure(
            _controller.Config.EnableTaskbarTransparency,
            _controller.Config.TaskbarMode,
            _controller.Config.TaskbarDarkOnMaximize);
    }

    private void OpenSettingsWindow()
    {
        if (_controller == null) return;
        if (_settings != null && _settings.IsVisible)
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(_controller, ResetWidgetPositions);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    private void ResetWidgetPositions()
    {
        if (_controller == null) return;
        var wa = SystemParameters.WorkArea;

        _controller.Config.LeftSystemPos.Left = wa.Left + 20;
        _controller.Config.LeftSystemPos.Top = wa.Top + 32;
        _controller.Config.LeftRecentPos.Left = wa.Left + 20;
        _controller.Config.LeftRecentPos.Top = wa.Top + 208;
        _controller.Config.LeftMostPos.Left = wa.Left + 20;
        _controller.Config.LeftMostPos.Top = wa.Top + 388;
        _controller.Config.RightHubPos.Left = Math.Max(300, wa.Right - 248 - 20);
        _controller.Config.RightHubPos.Top = wa.Top + 32;

        if (_leftSystem != null) { _leftSystem.Left = _controller.Config.LeftSystemPos.Left; _leftSystem.Top = _controller.Config.LeftSystemPos.Top; }
        if (_leftRecent != null) { _leftRecent.Left = _controller.Config.LeftRecentPos.Left; _leftRecent.Top = _controller.Config.LeftRecentPos.Top; }
        if (_leftMost != null) { _leftMost.Left = _controller.Config.LeftMostPos.Left; _leftMost.Top = _controller.Config.LeftMostPos.Top; }
        if (_rightHub != null) { _rightHub.Left = _controller.Config.RightHubPos.Left; _rightHub.Top = _controller.Config.RightHubPos.Top; }
        _controller.ConfigService.Save();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = CreateAuroraTrayIcon(),
            Text = "AuroraDesk 桌面工作台 (Alt + Space 秒搜)",
            Visible = true
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("🔍 全局秒搜 (Alt + Space)", null, (_, _) => Dispatcher.Invoke(() => _search?.OpenAndFocus()));
        menu.Items.Add("✨ 整理桌面新文件", null, (_, _) => Dispatcher.Invoke(() => _controller?.RunTidyDesktop(true)));
        menu.Items.Add("🪟 切换任务栏透明", null, (_, _) => Dispatcher.Invoke(() =>
        {
            if (_controller == null) return;
            _controller.Config.EnableTaskbarTransparency = !_controller.Config.EnableTaskbarTransparency;
            _controller.NotifyConfigUpdated();
        }));
        menu.Items.Add("👁️ 切换桌面图标显示/隐藏", null, (_, _) => Dispatcher.Invoke(() =>
        {
            if (_controller == null) return;
            _controller.Config.HideDesktopIcons = !_controller.Config.HideDesktopIcons;
            DesktopIconsHelper.SetSystemIconsHidden(_controller.Config.HideDesktopIcons);
            DesktopIconsHelper.SetDesktopIconsVisible(!_controller.Config.HideDesktopIcons);
            _controller.NotifyConfigUpdated();
        }));
        menu.Items.Add("⚙ 设置与分类定制中心", null, (_, _) => Dispatcher.Invoke(OpenSettingsWindow));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 AuroraDesk", null, (_, _) => Dispatcher.Invoke(Shutdown));

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettingsWindow);
    }

    private static Icon CreateAuroraTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = new GraphicsPath();
            path.AddEllipse(2, 2, 28, 28);
            using var brush = new LinearGradientBrush(new PointF(2, 2), new PointF(30, 30),
                Color.FromArgb(255, 167, 139, 250), Color.FromArgb(255, 56, 189, 248));
            g.FillPath(brush, path);
            using var font = new Font("Segoe UI", 14, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("A", font, Brushes.White, new RectangleF(2, 2, 28, 28), sf);
        }
        IntPtr hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbarService?.Dispose();
        _hotkey?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _controller?.ShutdownCleanup();
        if (_ownsInstanceMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
