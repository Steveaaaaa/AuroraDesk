using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AuroraDesk.App.Windows;

public sealed class CategoryEditDialog : Window
{
    public string CategoryTitle { get; private set; }
    public string CategorySubtitle { get; private set; }
    public string SelectedAccentHex { get; private set; }

    private readonly System.Windows.Controls.TextBox _titleBox;
    private readonly System.Windows.Controls.TextBox _subBox;

    public CategoryEditDialog(string windowTitle, string initialTitle, string initialSub, string initialHex, bool simpleRenameMode = false)
    {
        CategoryTitle = initialTitle;
        CategorySubtitle = initialSub;
        SelectedAccentHex = string.IsNullOrWhiteSpace(initialHex) ? "#A78BFA" : initialHex;

        Title = windowTitle;
        Width = 380;
        Height = simpleRenameMode ? 155 : 250;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 22, 34));
        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 245, 249));
        Topmost = true;

        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

        root.Children.Add(new TextBlock
        {
            Text = simpleRenameMode ? "新名称：" : "收纳盒名称：",
            FontSize = 12,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225)),
            Margin = new Thickness(0, 0, 0, 5)
        });

        _titleBox = new System.Windows.Controls.TextBox
        {
            Text = initialTitle,
            Height = 30,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 33, 50)),
            Foreground = System.Windows.Media.Brushes.White,
            CaretBrush = System.Windows.Media.Brushes.White,
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105)),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(_titleBox);

        _subBox = new System.Windows.Controls.TextBox
        {
            Text = initialSub,
            Height = 30,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 33, 50)),
            Foreground = System.Windows.Media.Brushes.White,
            CaretBrush = System.Windows.Media.Brushes.White,
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 14)
        };

        if (!simpleRenameMode)
        {
            root.Children.Add(new TextBlock
            {
                Text = "副标题说明（可选）：",
                FontSize = 12,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225)),
                Margin = new Thickness(0, 0, 0, 5)
            });
            root.Children.Add(_subBox);

            var colorPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            colorPanel.Children.Add(new TextBlock
            {
                Text = "色条：",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225)),
                Margin = new Thickness(0, 0, 8, 0)
            });

            string[] presets = ["#38BDF8", "#A78BFA", "#F472B6", "#34D399", "#FBBF24", "#FB923C", "#94A3B8"];
            foreach (var hex in presets)
            {
                var dot = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)),
                    BorderThickness = new Thickness(hex == SelectedAccentHex ? 2 : 0),
                    BorderBrush = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 0, 8, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = hex
                };
                dot.MouseLeftButtonUp += (_, _) =>
                {
                    SelectedAccentHex = hex;
                    foreach (var child in colorPanel.Children)
                    {
                        if (child is Border b && b.Tag is string h)
                        {
                            b.BorderThickness = new Thickness(h == hex ? 2 : 0);
                        }
                    }
                };
                colorPanel.Children.Add(dot);
            }
            root.Children.Add(colorPanel);
        }

        var btnPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var btnCancel = new System.Windows.Controls.Button { Content = "取消", Width = 72, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        btnCancel.Click += (_, _) => { DialogResult = false; Close(); };

        var btnOk = new System.Windows.Controls.Button
        {
            Content = "确定",
            Width = 84,
            Height = 28,
            IsDefault = true,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237)),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0)
        };
        btnOk.Click += (_, _) =>
        {
            string t = _titleBox.Text.Trim();
            if (string.IsNullOrEmpty(t))
            {
                System.Windows.MessageBox.Show("收纳盒名称不能为空！", "提示");
                return;
            }
            CategoryTitle = t;
            CategorySubtitle = _subBox.Text.Trim();
            DialogResult = true;
            Close();
        };

        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        root.Children.Add(btnPanel);

        Content = root;
        Loaded += (_, _) => { _titleBox.Focus(); _titleBox.SelectAll(); };
    }
}
