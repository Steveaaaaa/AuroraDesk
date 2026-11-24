using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AuroraDesk.App.Helpers;
using AuroraDesk.Core.Models;

namespace AuroraDesk.App.Windows;

public partial class RightHubWindow : Window
{
    private readonly AppController _controller;
    private readonly DispatcherTimer _sysTimer;
    private ulong _prevIdle, _prevKernel, _prevUser;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint dwLowDateTime, dwHighDateTime; }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    public RightHubWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        if (!_controller.Config.RightHubPos.HasCustomPosition)
        {
            _controller.Config.RightHubPos.Left = Math.Max(300, SystemParameters.WorkArea.Right - Width - 20);
            _controller.Config.RightHubPos.Top = SystemParameters.WorkArea.Top + 32;
        }

        WidgetWindowHelper.ConfigureDesktopWidget(this, _controller.Config.RightHubPos, () => _controller.ConfigService.Save());
        _controller.ConfigChanged += () => Dispatcher.Invoke(RefreshUI);
        _controller.DesktopItemsChanged += BuildCategoryCards;

        _sysTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sysTimer.Tick += (_, _) => UpdateSystemStats();

        Loaded += (_, _) =>
        {
            RefreshUI();
            UpdateSystemStats();
            _sysTimer.Start();
        };
    }

    public void RefreshUI()
    {
        AccentBar.Background = WidgetWindowHelper.ParseBrush(_controller.Config.AccentColorHex);
        BuildCategoryCards();
    }

    private void BuildCategoryCards()
    {
        CategoriesPanel.Children.Clear();
        foreach (var cat in _controller.Config.Categories)
        {
            CategoriesPanel.Children.Add(CreateCategoryCard(cat));
        }
    }

    private Border CreateCategoryCard(CategoryDef cat)
    {
        string folderPath = Path.Combine(_controller.Config.DesktopHubPath, cat.FolderName);
        int itemCount = Directory.Exists(folderPath)
            ? System.Linq.Enumerable.Count(Directory.GetFileSystemEntries(folderPath), p => !string.Equals(Path.GetFileName(p), "desktop.ini", StringComparison.OrdinalIgnoreCase))
            : 0;
        itemCount += _controller.DesktopCategories.GetEntries(folderPath).Count;
        bool allowFileMoves = !_controller.Config.KeepDesktopFilesInPlace;

        var card = new Border
        {
            Style = (Style)FindResource("GlassCardBtn"),
            Height = 56,
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(10, 8, 10, 8),
            AllowDrop = allowFileMoves,
            ToolTip = $"点击展开「{cat.Title}」（{itemCount} 项）" + (allowFileMoves ? " · 支持拖拽归档" : "")
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Border
        {
            Width = 3,
            Height = 30,
            CornerRadius = new CornerRadius(1.5),
            Background = WidgetWindowHelper.ParseBrush(cat.AccentHex),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        Grid.SetColumn(bar, 0);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = cat.Title,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 246, 252)),
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(cat.Subtitle) ? $"共 {itemCount} 项" : cat.Subtitle,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(186, 182, 208)),
            FontSize = 10,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(textStack, 1);

        var countBadge = new TextBlock
        {
            Text = itemCount.ToString(),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(170, 203, 213, 225)),
            FontSize = 10.5,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(countBadge, 2);

        grid.Children.Add(bar);
        grid.Children.Add(textStack);
        grid.Children.Add(countBadge);
        card.Child = grid;

        card.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _controller.RequestDrawer(folderPath, cat.Title, toggle: true);
        };

        // Drag & Drop directly onto category card with highlight feedback!
        card.DragEnter += (_, e) =>
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                card.BorderBrush = WidgetWindowHelper.ParseBrush(cat.AccentHex);
                card.BorderThickness = new Thickness(1.6);
                card.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(115, 42, 46, 74));
            }
        };
        card.DragLeave += (_, _) =>
        {
            card.ClearValue(Border.BorderBrushProperty);
            card.ClearValue(Border.BorderThicknessProperty);
            card.ClearValue(Border.BackgroundProperty);
        };
        card.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
            e.Handled = true;
        };
        card.Drop += (_, e) =>
        {
            card.ClearValue(Border.BorderBrushProperty);
            card.ClearValue(Border.BorderThicknessProperty);
            card.ClearValue(Border.BackgroundProperty);
            if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
            {
                MoveFilesToCategory(files, folderPath);
            }
        };

        card.ContextMenu = BuildCategoryContextMenu(cat, folderPath);

        return card;
    }

    private ContextMenu BuildCategoryContextMenu(CategoryDef cat, string folderPath)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu") };
        var list = _controller.Config.Categories;
        int idx = list.IndexOf(cat);

        var miOpen = new MenuItem { Header = "📂 在资源管理器中打开" };
        miOpen.Click += (_, _) => AppController.OpenInExplorer(folderPath);
        menu.Items.Add(miOpen);

        var miPaste = new MenuItem { Header = "📋 粘贴文件到此收纳盒 (剪贴板无文件)", IsEnabled = false };
        miPaste.Click += (_, _) => PasteClipboardFilesToCategory(folderPath);
        if (!_controller.Config.KeepDesktopFilesInPlace) menu.Items.Add(miPaste);

        menu.Opened += (_, _) =>
        {
            if (_controller.Config.KeepDesktopFilesInPlace) return;
            var (files, isCut) = ReadClipboardFileDrop();
            if (files.Length > 0)
            {
                miPaste.Header = isCut
                    ? $"✂️ 移动剪贴板文件到此收纳盒 ({files.Length} 项)"
                    : $"📋 粘贴剪贴板文件到此收纳盒 ({files.Length} 项)";
                miPaste.IsEnabled = true;
            }
            else
            {
                miPaste.Header = "📋 粘贴文件到此收纳盒 (剪贴板无文件)";
                miPaste.IsEnabled = false;
            }
        };

        var miRename = new MenuItem { Header = "✏️ 重命名 / 修改说明与色条" };
        miRename.Click += (_, _) => RenameCategory(cat, folderPath);
        menu.Items.Add(miRename);

        menu.Items.Add(new Separator());

        var miMoveUp = new MenuItem { Header = "⬆ 上移一位", IsEnabled = idx > 0 };
        miMoveUp.Click += (_, _) =>
        {
            if (idx > 0)
            {
                list.RemoveAt(idx);
                list.Insert(idx - 1, cat);
                _controller.NotifyConfigUpdated();
            }
        };
        menu.Items.Add(miMoveUp);

        var miMoveDown = new MenuItem { Header = "⬇ 下移一位", IsEnabled = idx >= 0 && idx < list.Count - 1 };
        miMoveDown.Click += (_, _) =>
        {
            if (idx >= 0 && idx < list.Count - 1)
            {
                list.RemoveAt(idx);
                list.Insert(idx + 1, cat);
                _controller.NotifyConfigUpdated();
            }
        };
        menu.Items.Add(miMoveDown);

        var miNew = new MenuItem { Header = "➕ 新建收纳盒" };
        miNew.Click += (_, _) => CreateNewCategory();
        menu.Items.Add(miNew);

        menu.Items.Add(new Separator());

        var miDelete = new MenuItem
        {
            Header = "🗑 删除收纳盒（图标释放回桌面）",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 165, 165))
        };
        miDelete.Click += (_, _) => DeleteCategoryAndReleaseToDesktop(cat, folderPath);
        if (!_controller.Config.KeepDesktopFilesInPlace) menu.Items.Add(miDelete);

        return menu;
    }

    private void RenameCategory(CategoryDef cat, string oldFolderPath)
    {
        var dlg = new CategoryEditDialog("重命名收纳盒", cat.Title, cat.Subtitle, cat.AccentHex);
        if (dlg.ShowDialog() != true) return;

        cat.Title = dlg.CategoryTitle;
        cat.Subtitle = dlg.CategorySubtitle;
        cat.AccentHex = dlg.SelectedAccentHex;

        // Also safely rename underlying folder if valid and non-conflicting
        string safeNewLeaf = SanitizeFolderName(dlg.CategoryTitle);
        string prefix = cat.FolderName.Length > 3 && char.IsDigit(cat.FolderName[0]) && char.IsDigit(cat.FolderName[1]) && cat.FolderName[2] == '_'
            ? cat.FolderName[..3]
            : "";
        string newFolderName = prefix + safeNewLeaf;
        string newFolderPath = Path.Combine(_controller.Config.DesktopHubPath, newFolderName);

        if (!string.IsNullOrWhiteSpace(safeNewLeaf) &&
            !string.Equals(oldFolderPath, newFolderPath, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(oldFolderPath) &&
            !Directory.Exists(newFolderPath))
        {
            try
            {
                _controller.RequestCloseDrawerIfMatches(oldFolderPath);
                Directory.Move(oldFolderPath, newFolderPath);
                cat.FolderName = newFolderName;
                _controller.UsageStats.UpdateFolderPrefix(oldFolderPath, newFolderPath);
            }
            catch { }
        }

        _controller.NotifyConfigUpdated();
    }

    private void CreateNewCategory()
    {
        var dlg = new CategoryEditDialog("新建收纳盒", "新建分类", "常用文件与快捷方式", "#A78BFA");
        if (dlg.ShowDialog() != true) return;

        int nextIdx = _controller.Config.Categories.Count + 1;
        string safeLeaf = SanitizeFolderName(dlg.CategoryTitle);
        if (string.IsNullOrWhiteSpace(safeLeaf)) safeLeaf = "自定义分类";
        string folderName = $"{nextIdx:00}_{safeLeaf}";
        string fullPath = Path.Combine(_controller.Config.DesktopHubPath, folderName);
        if (Directory.Exists(fullPath)) folderName = $"{nextIdx:00}_{safeLeaf}_{DateTime.Now:HHmmss}";

        _controller.Config.Categories.Add(new CategoryDef
        {
            FolderName = folderName,
            Title = dlg.CategoryTitle,
            Subtitle = dlg.CategorySubtitle,
            AccentHex = dlg.SelectedAccentHex
        });
        _controller.NotifyConfigUpdated();
    }

    private void DeleteCategoryAndReleaseToDesktop(CategoryDef cat, string folderPath)
    {
        string[] entries = Directory.Exists(folderPath) ? Directory.GetFileSystemEntries(folderPath) : [];
        var confirm = System.Windows.MessageBox.Show(
            $"确定删除收纳盒「{cat.Title}」吗？\n\n盒内现有的 {entries.Length} 个图标/文件【不会被删除】，将全部安全释放回你的桌面。",
            "删除收纳盒（安全释放）",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _controller.RequestCloseDrawerIfMatches(folderPath);

        string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        int released = 0;
        foreach (var src in entries)
        {
            string name = Path.GetFileName(src);
            if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            string dst = Path.Combine(userDesktop, name);
            if (File.Exists(dst) || Directory.Exists(dst))
            {
                string stem = Path.GetFileNameWithoutExtension(name);
                string ext = Path.GetExtension(name);
                dst = Path.Combine(userDesktop, $"{stem}_{DateTime.Now:MMdd_HHmmss}{ext}");
            }
            try
            {
                if (Directory.Exists(src)) Directory.Move(src, dst);
                else File.Move(src, dst);
                _controller.UsageStats.UpdateItemPath(src, dst);
                released++;
            }
            catch { }
        }

        // Remove empty category folder safely (never deletes non-empty folders!)
        if (Directory.Exists(folderPath))
        {
            try
            {
                string iniPath = Path.Combine(folderPath, "desktop.ini");
                if (File.Exists(iniPath))
                {
                    File.SetAttributes(iniPath, FileAttributes.Normal);
                    File.Delete(iniPath);
                }
                if (Directory.GetFileSystemEntries(folderPath).Length == 0)
                {
                    File.SetAttributes(folderPath, FileAttributes.Normal);
                    Directory.Delete(folderPath, false);
                }
            }
            catch { }
        }

        _controller.Config.Categories.Remove(cat);
        _controller.NotifyConfigUpdated();

        if (released > 0 && !AuroraDesk.Native.DesktopIconsHelper.AreDesktopIconsVisible())
        {
            var showIcons = System.Windows.MessageBox.Show(
                $"已删除收纳盒「{cat.Title}」，并将 {released} 个图标/文件安全释放到了桌面！\n\n当前桌面图标处于隐藏状态，是否立即显示桌面图标以查看它们？",
                "释放完成",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (showIcons == MessageBoxResult.Yes)
            {
                AuroraDesk.Native.DesktopIconsHelper.SetDesktopIconsVisible(true);
                _controller.Config.HideDesktopIcons = false;
                _controller.ConfigService.Save();
            }
        }
        else
        {
            System.Windows.MessageBox.Show(
                released > 0 ? $"已删除收纳盒「{cat.Title}」，并将 {released} 个图标/文件安全释放回了桌面。" : $"已删除空收纳盒「{cat.Title}」。",
                "删除完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static string SanitizeFolderName(string input)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(c.ToString(), "");
        }
        return input.Trim();
    }

    private void MoveFilesToCategory(string[] files, string destFolder)
    {
        if (_controller.Config.KeepDesktopFilesInPlace) return;
        if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);
        bool movedAny = false;
        foreach (var src in files)
        {
            try
            {
                string name = Path.GetFileName(src);
                string dst = Path.Combine(destFolder, name);
                if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(src)) Directory.Move(src, dst);
                else if (File.Exists(src)) File.Move(src, dst);
                _controller.UsageStats.UpdateItemPath(src, dst);
                movedAny = true;
            }
            catch { }
        }
        if (movedAny)
        {
            _controller.Search.RebuildIndex(_controller.Config);
            BuildCategoryCards();
        }
    }

    private static (string[] Files, bool IsCut) ReadClipboardFileDrop()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsFileDropList()) return ([], false);
            var list = System.Windows.Clipboard.GetFileDropList();
            if (list == null || list.Count == 0) return ([], false);

            var arr = new string[list.Count];
            list.CopyTo(arr, 0);

            bool isCut = false;
            var data = System.Windows.Clipboard.GetDataObject();
            if (data != null && data.GetData("Preferred DropEffect") is MemoryStream ms && ms.Length >= 4)
            {
                byte[] bytes = ms.ToArray();
                int effect = BitConverter.ToInt32(bytes, 0);
                // DROPEFFECT_MOVE = 2
                isCut = (effect & 2) != 0;
            }
            return (arr, isCut);
        }
        catch
        {
            return ([], false);
        }
    }

    private void PasteClipboardFilesToCategory(string destFolder)
    {
        if (_controller.Config.KeepDesktopFilesInPlace) return;
        var (files, isCut) = ReadClipboardFileDrop();
        if (files.Length == 0) return;

        if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);
        bool changed = false;

        foreach (var src in files)
        {
            try
            {
                if (!File.Exists(src) && !Directory.Exists(src)) continue;
                string name = Path.GetFileName(src);
                string dst = GetUniqueDestPath(destFolder, name, Directory.Exists(src));

                if (Directory.Exists(src))
                {
                    if (isCut)
                    {
                        if (string.Equals(Path.GetPathRoot(src), Path.GetPathRoot(dst), StringComparison.OrdinalIgnoreCase))
                        {
                            Directory.Move(src, dst);
                        }
                        else
                        {
                            CopyDirectoryRecursive(src, dst);
                            Directory.Delete(src, recursive: true);
                        }
                        _controller.UsageStats.UpdateItemPath(src, dst);
                    }
                    else
                    {
                        CopyDirectoryRecursive(src, dst);
                    }
                    changed = true;
                }
                else if (File.Exists(src))
                {
                    if (isCut)
                    {
                        File.Move(src, dst);
                        _controller.UsageStats.UpdateItemPath(src, dst);
                    }
                    else
                    {
                        File.Copy(src, dst, overwrite: false);
                    }
                    changed = true;
                }
            }
            catch { }
        }

        if (changed)
        {
            if (isCut)
            {
                try { System.Windows.Clipboard.Clear(); } catch { }
            }
            _controller.Search.RebuildIndex(_controller.Config);
            BuildCategoryCards();
        }
    }

    private static string GetUniqueDestPath(string destFolder, string name, bool isDir)
    {
        string dst = Path.Combine(destFolder, name);
        if (!File.Exists(dst) && !Directory.Exists(dst)) return dst;

        string stem = isDir ? name : Path.GetFileNameWithoutExtension(name);
        string ext = isDir ? "" : Path.GetExtension(name);
        for (int i = 2; i < 1000; i++)
        {
            string candidate = Path.Combine(destFolder, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(destFolder, $"{stem}_{DateTime.Now:HHmmss}{ext}");
    }

    private static void CopyDirectoryRecursive(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        foreach (string file in Directory.GetFiles(srcDir))
        {
            File.Copy(file, Path.Combine(dstDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (string sub in Directory.GetDirectories(srcDir))
        {
            CopyDirectoryRecursive(sub, Path.Combine(dstDir, Path.GetFileName(sub)));
        }
    }

    private void UpdateSystemStats()
    {
        string[] cnDays = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];
        DateText.Text = $"{DateTime.Now:MM/dd} · {cnDays[(int)DateTime.Now.DayOfWeek]}";

        int cpu = ReadCpuPercent();
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);

        long freeGb = 0;
        try
        {
            var sysDrive = new DriveInfo(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:");
            freeGb = sysDrive.AvailableFreeSpace / (1024L * 1024 * 1024);
        }
        catch { }

        SysStatusText.Text = $"CPU {cpu}%  ·  内存 {mem.dwMemoryLoad}%  ·  系统盘余 {freeGb}G";
    }

    private int ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt)) return 0;
        ulong idle = ((ulong)idleFt.dwHighDateTime << 32) | idleFt.dwLowDateTime;
        ulong kernel = ((ulong)kernelFt.dwHighDateTime << 32) | kernelFt.dwLowDateTime;
        ulong user = ((ulong)userFt.dwHighDateTime << 32) | userFt.dwLowDateTime;

        ulong idleDiff = idle - _prevIdle;
        ulong totalDiff = (kernel - _prevKernel) + (user - _prevUser);
        _prevIdle = idle; _prevKernel = kernel; _prevUser = user;

        if (totalDiff == 0) return 0;
        return Math.Clamp((int)((totalDiff - idleDiff) * 100 / totalDiff), 0, 100);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => WidgetWindowHelper.EnableDragMove(this, e, _controller.Config.RightHubPos, () => _controller.ConfigService.Save());

    private void BtnTidy_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; _controller.RunTidyDesktop(true); }
    private void BtnSearch_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; _controller.RequestSearch(); }
    private void BtnSettings_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; _controller.RequestSettings(); }
}
