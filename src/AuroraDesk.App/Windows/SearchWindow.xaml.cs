using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using AuroraDesk.App.Helpers;
using AuroraDesk.Core.Models;
using AuroraDesk.Native;

namespace AuroraDesk.App.Windows;

public partial class SearchWindow : Window
{
    private readonly AppController _controller;
    private List<SearchResultItem> _currentResults = [];

    public SearchWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

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
            DwmBlurHelper.EnableDesktopGlass(this, 16);
        };

        Deactivated += (_, _) => Hide();
    }

    public void OpenAndFocus()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + wa.Height * 0.22;

        QueryBox.Text = "";
        ResultsList.Items.Clear();
        ResultsList.Visibility = Visibility.Collapsed;
        Divider.Visibility = Visibility.Collapsed;
        Show();
        Activate();
        QueryBox.Focus();
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrEmpty(QueryBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        string q = QueryBox.Text.Trim();
        ResultsList.Items.Clear();

        if (string.IsNullOrEmpty(q))
        {
            ResultsList.Visibility = Visibility.Collapsed;
            Divider.Visibility = Visibility.Collapsed;
            return;
        }

        _currentResults = _controller.Search.Query(q, 9);
        if (_currentResults.Count == 0)
        {
            ResultsList.Visibility = Visibility.Collapsed;
            Divider.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var item in _currentResults)
        {
            ResultsList.Items.Add(CreateResultRow(item));
        }

        Divider.Visibility = Visibility.Visible;
        ResultsList.Visibility = Visibility.Visible;
        ResultsList.SelectedIndex = 0;
    }

    private Grid CreateResultRow(SearchResultItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (item.Kind == "Calc")
        {
            grid.Children.Add(new TextBlock
            {
                Text = "🧮",
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            });
        }
        else
        {
            string? png = ShellIconHelper.GetOrCacheIconPng(item.TargetPath, _controller.IconCacheDir);
            var bmp = ShellIconHelper.LoadCachedPng(png) ?? ShellIconHelper.ExtractIcon(item.TargetPath, 48);
            grid.Children.Add(new System.Windows.Controls.Image
            {
                Source = bmp,
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            });
        }

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = item.Title,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 250, 252)),
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
        });
        stack.Children.Add(new TextBlock
        {
            Text = item.Subtitle,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(164, 172, 192)),
            FontSize = 10.5,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
        });
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        var enterHint = new TextBlock
        {
            Text = "↵",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(140, 203, 213, 225)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(enterHint, 2);
        grid.Children.Add(enterHint);

        return grid;
    }

    private void LaunchSelected()
    {
        int idx = ResultsList.SelectedIndex;
        if (idx >= 0 && idx < _currentResults.Count)
        {
            var chosen = _currentResults[idx];
            Hide();
            _controller.LaunchTarget(chosen.TargetPath);
        }
    }

    private void QueryBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        else if (e.Key == Key.Enter) { LaunchSelected(); e.Handled = true; }
        else if (e.Key == Key.Down && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Min(ResultsList.Items.Count - 1, ResultsList.SelectedIndex + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 1);
            e.Handled = true;
        }
    }

    private void ResultsList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { LaunchSelected(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LaunchSelected();
}
