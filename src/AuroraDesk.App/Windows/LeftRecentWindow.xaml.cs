using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AuroraDesk.App.Helpers;
using AuroraDesk.Native;

namespace AuroraDesk.App.Windows;

public partial class LeftRecentWindow : Window
{
    private readonly AppController _controller;

    public LeftRecentWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        if (!_controller.Config.LeftRecentPos.HasCustomPosition)
        {
            _controller.Config.LeftRecentPos.Left = SystemParameters.WorkArea.Left + 20;
            _controller.Config.LeftRecentPos.Top = SystemParameters.WorkArea.Top + 208;
        }

        WidgetWindowHelper.ConfigureDesktopWidget(this, _controller.Config.LeftRecentPos, () => _controller.ConfigService.Save());
        _controller.ConfigChanged += () => Dispatcher.Invoke(RefreshUI);
        _controller.UsageStats.StatsChanged += () => Dispatcher.Invoke(RefreshSlots);
        Loaded += (_, _) => RefreshUI();
    }

    public void RefreshUI()
    {
        RootBorder.Background = WidgetWindowHelper.CreateGlassTint(_controller.Config.GlassOpacity, _controller.Config.AccentColorHex);
        AccentBar.Background = WidgetWindowHelper.ParseBrush(_controller.Config.AccentColorHex);
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        SlotsGrid.Children.Clear();
        var items = _controller.UsageStats.GetRecent(_controller.Config.RecentSlots);

        for (int i = 0; i < 8; i++)
        {
            if (i < items.Count)
            {
                var entry = items[i];
                string? pngPath = ShellIconHelper.GetOrCacheIconPng(entry.Path, _controller.IconCacheDir);
                var bmp = ShellIconHelper.LoadCachedPng(pngPath) ?? ShellIconHelper.ExtractIcon(entry.Path, 48);
                var timeStr = new DateTime(entry.LastUsedTicks).ToString("HH:mm");

                var card = new Border
                {
                    Style = (Style)FindResource("GlassCardBtn"),
                    Margin = new Thickness(2.5),
                    Padding = new Thickness(2, 5, 2, 4),
                    ToolTip = $"{entry.Name} · 最近使用 {timeStr}"
                };

                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new System.Windows.Controls.Image
                {
                    Source = bmp,
                    Width = 26,
                    Height = 26,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                });
                stack.Children.Add(new TextBlock
                {
                    Text = entry.Name,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 238, 248)),
                    FontSize = 9.5,
                    FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(2, 3, 2, 0)
                });

                card.Child = stack;
                card.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    _controller.LaunchTarget(entry.Path);
                };

                var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu") };
                var miOpen = new MenuItem { Header = "打开" };
                miOpen.Click += (_, _) => _controller.LaunchTarget(entry.Path);
                var miLocate = new MenuItem { Header = "打开所在目录" };
                miLocate.Click += (_, _) => AppController.OpenInExplorer(entry.Path);
                var miRemove = new MenuItem { Header = "从最近记录移除" };
                miRemove.Click += (_, _) => _controller.UsageStats.RemoveFromRecent(entry.Path);
                menu.Items.Add(miOpen);
                menu.Items.Add(miLocate);
                menu.Items.Add(new Separator());
                menu.Items.Add(miRemove);
                card.ContextMenu = menu;

                SlotsGrid.Children.Add(card);
            }
            else
            {
                SlotsGrid.Children.Add(new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(26, 20, 22, 34)),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(2.5),
                    ToolTip = "点击收纳箱应用后自动填充"
                });
            }
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WidgetWindowHelper.EnableDragMove(this, e, _controller.Config.LeftRecentPos, () => _controller.ConfigService.Save());
    }
}
