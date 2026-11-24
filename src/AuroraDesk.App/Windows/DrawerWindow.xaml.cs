using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using AuroraDesk.App.Helpers;
using AuroraDesk.Core.Helpers;
using AuroraDesk.Core.Services;
using AuroraDesk.Native;

namespace AuroraDesk.App.Windows;

public partial class DrawerWindow : Window
{
    private readonly AppController _controller;
    private readonly OutsideClickMonitor _outsideClicks;
    private string _openedFolder = "";
    private string _currentFolder = "";
    private string _currentTitle = "";
    private readonly Stack<(string Folder, string Title, bool IsDesktopFolder)> _navigation = new();
    private bool _isDesktopFolder;
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;
    private int _visibilityVersion;
    private bool _closing;
    private double _dockOffset;

    public DrawerWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        _outsideClicks = new OutsideClickMonitor(QueueOutsideClose);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _outsideClicks.Start();
            else _outsideClicks.Stop();
        };
        Closed += (_, _) => _outsideClicks.Stop();

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        SourceInitialized += (_, _) =>
        {
            DesktopWindowPin.MakeToolWindowOnly(this);
        };
        Loaded += (_, _) =>
        {
            DwmBlurHelper.EnableDesktopGlass(this, 18);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseAnimated();
        };

        Drop += DrawerWindow_Drop;
        DragOver += (_, e) =>
        {
            e.Effects = !_controller.Config.KeepDesktopFilesInPlace && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
                ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
            e.Handled = true;
        };

        _controller.ConfigChanged += () => Dispatcher.Invoke(() =>
        {
            if (!IsVisible) return;
            if (_isDesktopFolder && !_controller.Config.KeepDesktopFilesInPlace) HideDrawer();
            else
            {
                ApplyTheme();
                PopulateList();
            }
        });
        _controller.DesktopItemsChanged += () => { if (IsVisible && !_closing) PopulateList(); };
    }

    public void CloseIfShowingFolder(string folderPath)
    {
        if (IsVisible && _currentFolder.StartsWith(folderPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            HideDrawer();
        }
    }

    public void OpenFolder(string folderPath, string title, Window anchorRightHub, bool toggle)
    {
        string openedFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        if (toggle && IsVisible && !_closing && string.Equals(_openedFolder, openedFolder, StringComparison.OrdinalIgnoreCase))
        {
            CloseAnimated();
            return;
        }

        _visibilityVersion++;
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
        _openedFolder = openedFolder;
        _currentFolder = folderPath;
        _currentTitle = title;
        _navigation.Clear();
        _isDesktopFolder = false;
        TitleText.Text = title;

        var wa = SystemParameters.WorkArea;
        double targetLeft = anchorRightHub.Left - Width - 10;
        if (targetLeft < wa.Left + 10) targetLeft = anchorRightHub.Left + anchorRightHub.Width + 10;
        _dockOffset = targetLeft < anchorRightHub.Left ? 12 : -12;
        Left = targetLeft;
        Top = anchorRightHub.Top;
        Height = anchorRightHub.Height;

        FilterBox.Text = "";
        ApplyTheme();
        PopulateList(scrollToTop: true);

        if (!IsVisible)
        {
            bool animate = SystemParameters.ClientAreaAnimation;
            ResetMotion(animate ? 0 : 1, animate ? _dockOffset : 0);
            Show();
            if (animate) AnimateDrawer(1, 0, 190, EasingMode.EaseOut);
        }
        else
        {
            if (_closing)
            {
                _closing = false;
                RootBorder.IsHitTestVisible = true;
                AnimateDrawer(1, 0, 150, EasingMode.EaseOut);
            }
            Activate();
        }
    }

    private void QueueOutsideClose()
    {
        int version = _visibilityVersion;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (version == _visibilityVersion) CloseAnimated();
        }));
    }

    private void ApplyTheme()
    {
        AccentBar.Background = WidgetWindowHelper.ParseBrush(_controller.Config.AccentColorHex);
    }

    private void ResetMotion(double opacity, double offset)
    {
        RootBorder.BeginAnimation(OpacityProperty, null);
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        RootBorder.Opacity = opacity;
        SlideTransform.X = offset;
    }

    private void AnimateDrawer(double opacity, double offset, int milliseconds, EasingMode easing, Action? completed = null)
    {
        double fromOpacity = RootBorder.Opacity;
        double fromOffset = SlideTransform.X;
        ResetMotion(opacity, offset);
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var ease = new CubicEase { EasingMode = easing };
        var fade = new DoubleAnimation(fromOpacity, opacity, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        if (completed != null) fade.Completed += (_, _) => completed();
        RootBorder.BeginAnimation(OpacityProperty, fade);
        SlideTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(fromOffset, offset, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    private void CloseAnimated()
    {
        if (!IsVisible || _closing) return;
        if (!SystemParameters.ClientAreaAnimation)
        {
            HideDrawer();
            return;
        }

        _closing = true;
        RootBorder.IsHitTestVisible = false;
        int version = ++_visibilityVersion;
        AnimateDrawer(0, _dockOffset, 135, EasingMode.EaseIn, () =>
        {
            if (version == _visibilityVersion) HideDrawer();
        });
    }

    private void HideDrawer()
    {
        _visibilityVersion++;
        _closing = false;
        Hide();
        ResetMotion(1, 0);
        RootBorder.IsHitTestVisible = true;
    }

    private void PopulateList(bool scrollToTop = false)
    {
        ItemsPanel.Children.Clear();
        if (string.IsNullOrEmpty(_currentFolder)) return;

        string filter = FilterBox.Text.Trim();
        var desktopEntries = _controller.DesktopCategories.GetEntries(_currentFolder);
        var entries = ReadFolderEntries()
            .Concat(desktopEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p =>
            {
                string leaf = Path.GetFileName(p);
                if (string.Equals(leaf, "desktop.ini", StringComparison.OrdinalIgnoreCase)) return false;
                try
                {
                    var attr = File.GetAttributes(p);
                    if ((attr & FileAttributes.Hidden) != 0 && (attr & FileAttributes.System) != 0) return false;
                }
                catch { }
                return true;
            });

        int shown = 0;
        foreach (var path in _controller.UsageStats.SortDrawerEntries(entries))
        {
            string displayName = UsageStatsService.CleanDisplayName(path);
            if (!string.IsNullOrEmpty(filter) && PinyinHelper.MatchScore(filter, displayName) == 0) continue;

            shown++;
            ItemsPanel.Children.Add(CreateItemRow(path, displayName, _isDesktopFolder || desktopEntries.Contains(path, StringComparer.OrdinalIgnoreCase)));
        }

        if (shown == 0)
        {
            ItemsPanel.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrEmpty(filter) ? "没有匹配的项目" : _controller.Config.KeepDesktopFilesInPlace
                    ? "此分类暂无文件" : "此分类暂无文件（可直接拖拽文件进来）",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 203, 213, 225)),
                FontSize = 11.5,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            });
        }

        bool canGoUp = !_currentFolder.TrimEnd('\\').Equals(_controller.Config.DesktopHubPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(_currentFolder.TrimEnd('\\'))?.Equals(_controller.Config.DesktopHubPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == false;
        BtnUp.Visibility = _navigation.Count > 0 || (!_isDesktopFolder && canGoUp) ? Visibility.Visible : Visibility.Collapsed;
        FooterHint.Text = _controller.Config.KeepDesktopFilesInPlace ? "单击打开 · 右键查看文件位置" : "单击启动 · 拖到右侧卡片换分类 · 右键管理";
        if (scrollToTop) ItemsScrollViewer.ScrollToTop();
    }

    private string[] ReadFolderEntries()
    {
        if (!Directory.Exists(_currentFolder)) return [];
        try { return Directory.GetFileSystemEntries(_currentFolder); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private Border CreateItemRow(string path, string displayName, bool isDesktopItem)
    {
        string? png = ShellIconHelper.GetOrCacheIconPng(path, _controller.IconCacheDir);
        var bmp = ShellIconHelper.LoadCachedPng(png) ?? ShellIconHelper.ExtractIcon(path, 48);

        var row = new Border
        {
            Style = (Style)FindResource("GlassCardBtn"),
            Height = 46,
            Margin = new Thickness(0, 0, 0, 5),
            Padding = new Thickness(10, 0, 10, 0)
        };
        if (isDesktopItem) row.ToolTip = $"桌面原件 · {path}";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new System.Windows.Controls.Image
        {
            Source = bmp,
            Width = 26,
            Height = 26,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        });

        var title = new TextBlock
        {
            Text = displayName,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 244, 250)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        if (Directory.Exists(path))
        {
            var arrow = new TextBlock
            {
                Text = "›",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 203, 213, 225)),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(arrow);
        }

        row.Child = grid;

        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        };

        row.PreviewMouseMove += (_, e) =>
        {
            if (_controller.Config.KeepDesktopFilesInPlace || e.LeftButton != MouseButtonState.Pressed || _isDragging) return;
            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isDragging = true;
                var preview = new DragPreviewWindow(bmp, displayName);
                void OnFeedback(object? s, System.Windows.GiveFeedbackEventArgs fe)
                {
                    preview.UpdatePositionToCursor();
                }

                row.Opacity = 0.35;
                row.GiveFeedback += OnFeedback;
                preview.Show();
                try
                {
                    var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { path });
                    DragDrop.DoDragDrop(row, data, System.Windows.DragDropEffects.Move);
                }
                finally
                {
                    row.GiveFeedback -= OnFeedback;
                    row.Opacity = 1.0;
                    preview.Close();
                }

                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    PopulateList();
                }
            }
        };

        row.MouseLeftButtonUp += (_, e) =>
        {
            if (_isDragging) return;
            e.Handled = true;
            OpenItem(path, displayName, isDesktopItem);
        };

        row.ContextMenu = BuildRowContextMenu(path, isDesktopItem);
        return row;
    }

    private void OpenItem(string path, string displayName, bool isDesktopItem, bool asAdmin = false)
    {
        if (Directory.Exists(path))
        {
            _controller.UsageStats.RecordFolderOpen(path);
            _navigation.Push((_currentFolder, _currentTitle, _isDesktopFolder));
            _isDesktopFolder = isDesktopItem;
            _currentFolder = path;
            _currentTitle = displayName;
            TitleText.Text = displayName;
            FilterBox.Text = "";
        }
        else
        {
            _controller.LaunchTarget(path, asAdmin);
        }

        PopulateList(scrollToTop: true);
    }

    private ContextMenu BuildRowContextMenu(string path, bool isDesktopItem)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu") };

        var miOpen = new MenuItem { Header = "🚀 打开" };
        miOpen.Click += (_, _) => OpenItem(path, UsageStatsService.CleanDisplayName(path), isDesktopItem);
        menu.Items.Add(miOpen);

        if (!Directory.Exists(path))
        {
            var miAdmin = new MenuItem { Header = "🛡 以管理员身份运行" };
            miAdmin.Click += (_, _) => OpenItem(path, UsageStatsService.CleanDisplayName(path), isDesktopItem, asAdmin: true);
            menu.Items.Add(miAdmin);
        }

        menu.Items.Add(new Separator());

        var miCopy = new MenuItem { Header = "📋 复制文件（可 Ctrl+V 粘贴）" };
        miCopy.Click += (_, _) =>
        {
            var sc = new System.Collections.Specialized.StringCollection { path };
            System.Windows.Clipboard.SetFileDropList(sc);
        };
        menu.Items.Add(miCopy);

        var miExplore = new MenuItem { Header = "📂 打开所在位置" };
        miExplore.Click += (_, _) => AppController.OpenInExplorer(path);
        menu.Items.Add(miExplore);

        if (isDesktopItem)
        {
            var properties = new MenuItem { Header = "属性" };
            properties.Click += (_, _) => ShellFileOpsHelper.ShowProperties(path);
            menu.Items.Add(properties);
            return menu;
        }

        var miRename = new MenuItem { Header = "✏️ 重命名" };
        miRename.Click += (_, _) => RenameEntry(path);
        menu.Items.Add(miRename);

        var miMove = new MenuItem { Header = "📦 移动到其他分类" };
        foreach (var cat in _controller.Config.Categories)
        {
            string destDir = Path.Combine(_controller.Config.DesktopHubPath, cat.FolderName);
            if (string.Equals(destDir.TrimEnd('\\'), _currentFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
            var sub = new MenuItem { Header = cat.Title };
            sub.Click += (_, _) =>
            {
                if (_controller.Config.KeepDesktopFilesInPlace) return;
                try
                {
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    string dst = Path.Combine(destDir, Path.GetFileName(path));
                    if (Directory.Exists(path)) Directory.Move(path, dst);
                    else File.Move(path, dst);
                    _controller.UsageStats.UpdateItemPath(path, dst);
                    _controller.NotifyConfigUpdated();
                    PopulateList();
                }
                catch { }
            };
            miMove.Items.Add(sub);
        }
        if (!_controller.Config.KeepDesktopFilesInPlace) menu.Items.Add(miMove);

        menu.Items.Add(new Separator());

        var miProps = new MenuItem { Header = "ℹ 属性" };
        miProps.Click += (_, _) => ShellFileOpsHelper.ShowProperties(path);
        menu.Items.Add(miProps);

        var miDelete = new MenuItem
        {
            Header = "🗑 删除（移至回收站）",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 165, 165))
        };
        miDelete.Click += (_, _) => DeleteEntryToRecycleBin(path);
        menu.Items.Add(miDelete);

        return menu;
    }

    private void RenameEntry(string path)
    {
        bool isDir = Directory.Exists(path);
        string ext = isDir ? "" : Path.GetExtension(path);
        string oldStem = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);

        var dlg = new CategoryEditDialog("重命名项目", oldStem, "", "#A78BFA", simpleRenameMode: true);
        if (dlg.ShowDialog() != true) return;

        string newStem = dlg.CategoryTitle.Trim();
        foreach (char c in Path.GetInvalidFileNameChars()) newStem = newStem.Replace(c.ToString(), "");
        if (string.IsNullOrEmpty(newStem) || string.Equals(newStem, oldStem, StringComparison.Ordinal)) return;

        string? dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir)) return;
        string newPath = Path.Combine(dir, newStem + ext);
        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            System.Windows.MessageBox.Show("同目录下已存在同名文件！", "无法重命名");
            return;
        }

        try
        {
            if (isDir) Directory.Move(path, newPath);
            else File.Move(path, newPath);
            _controller.UsageStats.UpdateItemPath(path, newPath);
            _controller.NotifyConfigUpdated();
            PopulateList();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"重命名失败：{ex.Message}", "错误");
        }
    }

    private void DeleteEntryToRecycleBin(string path)
    {
        string displayName = UsageStatsService.CleanDisplayName(path);
        var res = System.Windows.MessageBox.Show(
            $"确定将「{displayName}」移至系统回收站吗？\n（删除后仍可在回收站还原）",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        if (ShellFileOpsHelper.MoveToRecycleBin(path))
        {
            _controller.UsageStats.RemoveFromRecent(path);
            _controller.UsageStats.ResetItemCount(path);
            _controller.NotifyConfigUpdated();
            PopulateList();
        }
    }

    private void DrawerWindow_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (_controller.Config.KeepDesktopFilesInPlace) return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files || !Directory.Exists(_currentFolder)) return;
        foreach (var src in files)
        {
            try
            {
                string dst = Path.Combine(_currentFolder, Path.GetFileName(src));
                if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(src)) Directory.Move(src, dst);
                else if (File.Exists(src)) File.Move(src, dst);
                else continue;
                _controller.UsageStats.UpdateItemPath(src, dst);
            }
            catch { }
        }
        _controller.NotifyConfigUpdated();
        PopulateList();
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterPlaceholder.Visibility = string.IsNullOrEmpty(FilterBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        PopulateList();
    }

    private void BtnUp_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_navigation.TryPop(out var previous))
        {
            (_currentFolder, _currentTitle, _isDesktopFolder) = previous;
            TitleText.Text = _currentTitle;
            FilterBox.Text = "";
            PopulateList(scrollToTop: true);
            return;
        }
        string? parent = Path.GetDirectoryName(_currentFolder.TrimEnd('\\'));
        if (!string.IsNullOrEmpty(parent) && parent.StartsWith(_controller.Config.DesktopHubPath, StringComparison.OrdinalIgnoreCase)
            && !parent.Equals(_controller.Config.DesktopHubPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentFolder = parent;
            _currentTitle = Path.GetFileName(parent);
            TitleText.Text = _currentTitle;
            PopulateList(scrollToTop: true);
        }
    }

    private void BtnOpenDir_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; AppController.OpenInExplorer(_currentFolder); }
    private void BtnClose_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; CloseAnimated(); }
}
