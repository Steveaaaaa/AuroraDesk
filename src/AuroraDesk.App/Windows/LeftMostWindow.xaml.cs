using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AuroraDesk.App.Helpers;
using AuroraDesk.Native;

namespace AuroraDesk.App.Windows;

public partial class LeftMostWindow : Window
{
    private readonly AppController _controller;

    public LeftMostWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        if (!_controller.Config.LeftMostPos.HasCustomPosition)
        {
            _controller.Config.LeftMostPos.Left = SystemParameters.WorkArea.Left + 20;
            _controller.Config.LeftMostPos.Top = SystemParameters.WorkArea.Top + 388;
        }

        WidgetWindowHelper.ConfigureDesktopWidget(this, _controller.Config.LeftMostPos, () => _controller.ConfigService.Save());
        _controller.ConfigChanged += () => Dispatcher.Invoke(RefreshUI);
        _controller.UsageStats.StatsChanged += () => Dispatcher.Invoke(RefreshSlots);
        Loaded += (_, _) => RefreshUI();
    }

    public void RefreshUI()
    {
        RootBorder.Background = WidgetWindowHelper.CreateGlassTint(_controller.Config.GlassOpacity, _controller.Config.AccentColorHex);
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        SlotsGrid.Children.Clear();
        var items = _controller.UsageStats.GetMostUsed(_controller.Config.MostSlots);

        for (int i = 0; i < 8; i++)
        {
            if (i < items.Count)
            {
                var entry = items[i];
                string? pngPath = ShellIconHelper.GetOrCacheIconPng(entry.Path, _controller.IconCacheDir);
                var bmp = ShellIconHelper.LoadCachedPng(pngPath) ?? ShellIconHelper.ExtractIcon(entry.Path, 48);

                var card = new Border
                {
                    Style = (Style)FindResource("GlassCardBtn"),
                    Margin = new Thickness(2.5),
                    Padding = new Thickness(2, 5, 2, 4),
                    ToolTip = $"{entry.Name} · 累计 {entry.Count} 次" + (entry.IsPinned ? "（已置顶）" : "")
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
                    Text = entry.IsPinned ? $"★ {entry.Count}次" : $"{entry.Count}次",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(110, 231, 183)),
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0)
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
                var miPin = new MenuItem { Header = entry.IsPinned ? "取消置顶" : "置顶此应用" };
                miPin.Click += (_, _) => _controller.UsageStats.TogglePin(entry.Path);
                var miReset = new MenuItem { Header = "清零统计次数" };
                miReset.Click += (_, _) => _controller.UsageStats.ResetItemCount(entry.Path);
                menu.Items.Add(miOpen);
                menu.Items.Add(miPin);
                menu.Items.Add(new Separator());
                menu.Items.Add(miReset);
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
                    ToolTip = "统计累计使用次数"
                });
            }
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WidgetWindowHelper.EnableDragMove(this, e, _controller.Config.LeftMostPos, () => _controller.ConfigService.Save());
    }
}
