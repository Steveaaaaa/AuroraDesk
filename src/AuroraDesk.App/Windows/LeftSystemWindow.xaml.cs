using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AuroraDesk.App.Helpers;
using AuroraDesk.Native;

namespace AuroraDesk.App.Windows;

public partial class LeftSystemWindow : Window
{
    private readonly AppController _controller;

    public LeftSystemWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        if (!_controller.Config.LeftSystemPos.HasCustomPosition)
        {
            _controller.Config.LeftSystemPos.Left = SystemParameters.WorkArea.Left + 20;
            _controller.Config.LeftSystemPos.Top = SystemParameters.WorkArea.Top + 32;
        }

        WidgetWindowHelper.ConfigureDesktopWidget(this, _controller.Config.LeftSystemPos, () => _controller.ConfigService.Save());
        _controller.ConfigChanged += ApplyTheme;
        Loaded += (_, _) => { ApplyTheme(); PopulateItems(); };
    }

    public void ApplyTheme()
    {
        RootBorder.Background = WidgetWindowHelper.CreateGlassTint(_controller.Config.GlassOpacity, _controller.Config.AccentColorHex);
    }

    private void PopulateItems()
    {
        IconsGrid.Children.Clear();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string downloads = Path.Combine(home, "Downloads");

        (string label, string iconFile, string target)[] items =
        [
            ("此电脑", "sys_pc.png", "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
            ("回收站", "sys_trash.png", "shell:::{645FF040-5081-101B-9F08-00AA002F954E}"),
            ("个人",   "sys_home.png", home),
            ("下载",   "sys_down.png", downloads),
            ("设置",   "sys_settings.png", "ms-settings:"),
            ("任务",   "sys_taskmgr.png", "taskmgr.exe")
        ];

        foreach (var (label, iconFile, target) in items)
        {
            string iconPath = Path.Combine(_controller.IconCacheDir, iconFile);
            var imgSource = ShellIconHelper.LoadCachedPng(iconPath) ?? ShellIconHelper.ExtractIcon(target, 48);

            var card = new Border
            {
                Style = (Style)FindResource("GlassCardBtn"),
                Margin = new Thickness(3),
                Padding = new Thickness(2, 4, 2, 3),
                ToolTip = label
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new System.Windows.Controls.Image
            {
                Source = imgSource,
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(235, 240, 248)),
                FontSize = 10,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });

            card.Child = stack;
            card.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _controller.LaunchTarget(target, trackUsage: false);
            };
            card.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                var menu = BuildSystemIconMenu(label, target, home, downloads);
                menu.PlacementTarget = card;
                menu.IsOpen = true;
            };
            IconsGrid.Children.Add(card);
        }
    }

    private ContextMenu BuildSystemIconMenu(string label, string target, string home, string downloads)
    {
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("DarkContextMenu")
        };

        void AddItem(string header, Action onClick, bool enabled = true, bool danger = false)
        {
            var mi = new MenuItem
            {
                Header = header,
                IsEnabled = enabled
            };
            if (danger)
            {
                mi.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 165, 165));
            }
            mi.Click += (_, _) => onClick();
            menu.Items.Add(mi);
        }

        switch (label)
        {
            case "此电脑":
                AddItem("💻 打开此电脑", () => _controller.LaunchTarget(target, false));
                menu.Items.Add(new Separator());
                AddItem("🛠️ 计算机管理", () => LaunchShell("compmgmt.msc"));
                AddItem("💾 磁盘管理", () => LaunchShell("diskmgmt.msc"));
                AddItem("🔌 设备管理器", () => LaunchShell("devmgmt.msc"));
                menu.Items.Add(new Separator());
                AddItem("ℹ️ 系统属性 / 关于", () => LaunchShell("ms-settings:about"));
                AddItem("⚙️ 高级系统设置", () => LaunchShell("SystemPropertiesAdvanced.exe"));
                break;

            case "回收站":
                AddItem("📂 打开回收站", () => _controller.LaunchTarget(target, false));
                var (count, bytes) = ShellFileOpsHelper.QueryRecycleBin();
                string sizeText = FormatBytes(bytes);
                string emptyLabel = count > 0
                    ? $"🗑️ 清空回收站 ({count} 项 · {sizeText})"
                    : "🗑️ 清空回收站 (已空)";
                AddItem(emptyLabel, () =>
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    ShellFileOpsHelper.EmptyRecycleBin(hwnd);
                }, enabled: count > 0, danger: count > 0);
                menu.Items.Add(new Separator());
                AddItem("ℹ️ 回收站属性", () => ShellFileOpsHelper.ShowProperties(target));
                break;

            case "个人":
                AddItem("📂 打开个人文件夹", () => _controller.LaunchTarget(home, false));
                menu.Items.Add(new Separator());
                AddItem("📄 文档", () => _controller.LaunchTarget(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), false));
                AddItem("🖼️ 图片", () => _controller.LaunchTarget(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), false));
                AddItem("📦 桌面收纳根目录", () => _controller.LaunchTarget(_controller.Config.DesktopHubPath, false));
                menu.Items.Add(new Separator());
                AddItem("📋 复制路径", () => CopyTextSafe(home));
                AddItem("ℹ️ 属性", () => ShellFileOpsHelper.ShowProperties(home));
                break;

            case "下载":
                AddItem("📂 打开下载文件夹", () => _controller.LaunchTarget(downloads, false));
                AddItem("⌨️ 在终端中打开", () => OpenTerminalIn(downloads));
                menu.Items.Add(new Separator());
                AddItem("📋 复制路径", () => CopyTextSafe(downloads));
                AddItem("ℹ️ 属性", () => ShellFileOpsHelper.ShowProperties(downloads));
                break;

            case "设置":
                AddItem("⚙️ 打开 Windows 设置", () => LaunchShell("ms-settings:"));
                menu.Items.Add(new Separator());
                AddItem("🌐 网络和 Internet", () => LaunchShell("ms-settings:network"));
                AddItem("🎧 蓝牙和其他设备", () => LaunchShell("ms-settings:bluetooth"));
                AddItem("🔊 声音设置", () => LaunchShell("ms-settings:sound"));
                AddItem("📦 已安装的应用", () => LaunchShell("ms-settings:appsfeatures"));
                AddItem("🎛️ 传统控制面板", () => LaunchShell("control.exe"));
                menu.Items.Add(new Separator());
                AddItem("✨ AuroraDesk 设置中心", () => _controller.RequestSettings());
                break;

            case "任务":
                AddItem("📊 打开任务管理器", () => LaunchShell("taskmgr.exe"));
                AddItem("🛡️ 以管理员身份运行", () => LaunchShell("taskmgr.exe", asAdmin: true));
                menu.Items.Add(new Separator());
                AddItem("📈 资源监视器", () => LaunchShell("perfmon.exe", "/res"));
                AddItem("⚙️ 服务管理 (services.msc)", () => LaunchShell("services.msc"));
                AddItem("🚀 开机启动项管理", () => LaunchShell("ms-settings:startupapps"));
                break;
        }

        return menu;
    }

    private static void LaunchShell(string file, string args = "", bool asAdmin = false)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = true };
            if (asAdmin) psi.Verb = "runas";
            Process.Start(psi);
        }
        catch { }
    }

    private static void OpenTerminalIn(string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{dir}\"") { UseShellExecute = true });
        }
        catch
        {
            LaunchShell("cmd.exe", $"/k cd /d \"{dir}\"");
        }
    }

    private static void CopyTextSafe(string text)
    {
        try { System.Windows.Clipboard.SetText(text); } catch { }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:0.#} MB";
        return $"{mb / 1024.0:0.##} GB";
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WidgetWindowHelper.EnableDragMove(this, e, _controller.Config.LeftSystemPos, () => _controller.ConfigService.Save());
    }
}
