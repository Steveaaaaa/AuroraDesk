using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using AuroraDesk.App.Helpers;
using AuroraDesk.Core.Models;
using AuroraDesk.Core.Services;
using AuroraDesk.Native;

namespace AuroraDesk.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private readonly ObservableCollection<CategoryDef> _categories;
    private readonly Action _resetPositionsAction;
    private readonly DispatcherTimer _storageSaveDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _loading = true;
    private bool _syncingStorageRange;
    private string _pendingTemplateId;

    public SettingsWindow(AppController controller, Action resetPositionsAction)
    {
        _controller = controller;
        _resetPositionsAction = resetPositionsAction;
        _pendingTemplateId = controller.Config.TemplateId;
        InitializeComponent();

        _categories = new ObservableCollection<CategoryDef>(
            _controller.Config.Categories.Select(c => new CategoryDef
            {
                FolderName = c.FolderName,
                Title = c.Title,
                Subtitle = c.Subtitle,
                AccentHex = c.AccentHex,
                Keywords = c.Keywords,
                Extensions = c.Extensions
            }));

        CategoriesGrid.ItemsSource = _categories;
        OpacitySlider.Value = _controller.Config.GlassOpacity;
        OpacityValueText.Text = $"{(int)(OpacitySlider.Value * 100)}%";
        SyncToggles();
        var (minimum, maximum) = _controller.Config.GetStorageOpacityRange();
        StorageMinSlider.Minimum = AppConfig.StorageOpacityFloor;
        StorageMinSlider.Maximum = AppConfig.StorageOpacityCeiling;
        StorageMaxSlider.Minimum = AppConfig.StorageOpacityFloor;
        StorageMaxSlider.Maximum = AppConfig.StorageOpacityCeiling;
        StorageMinSlider.Value = minimum;
        StorageMaxSlider.Value = maximum;
        UpdateStorageRangeLabels();
        _loading = false;
        _controller.ConfigChanged += SyncToggles;
        _storageSaveDelay.Tick += (_, _) => SavePendingStorageRange();
        Closed += (_, _) =>
        {
            SavePendingStorageRange();
            _controller.ConfigChanged -= SyncToggles;
        };
    }

    private void SyncToggles()
    {
        ChkAutoStart.IsChecked = _controller.Config.AutoStart;
        ChkHideIcons.IsChecked = _controller.Config.HideDesktopIcons;
        ChkAutoTidy.IsChecked = _controller.Config.AutoTidyDesktop;
        ChkKeepDesktopFiles.IsChecked = _controller.Config.KeepDesktopFilesInPlace;
        ChkAutoTidy.IsEnabled = !_controller.Config.KeepDesktopFilesInPlace;
        ChkShowRecent.IsChecked = _controller.Config.ShowRecentWidget;
        ChkShowMost.IsChecked = _controller.Config.ShowMostWidget;
        ChkAdaptiveStorage.IsChecked = _controller.Config.EnableAdaptiveStorageOpacity;

        ChkTaskbarEnable.IsChecked = _controller.Config.EnableTaskbarTransparency;
        ChkTaskbarDarkMax.IsChecked = _controller.Config.TaskbarDarkOnMaximize;
        string tbMode = (_controller.Config.TaskbarMode ?? "Clear").ToLowerInvariant();
        RbTaskbarClear.IsChecked = tbMode is not ("translucent" or "acrylic");
        RbTaskbarTranslucent.IsChecked = tbMode == "translucent";
        RbTaskbarAcrylic.IsChecked = tbMode == "acrylic";
    }

    private void AdaptiveStorage_Click(object sender, RoutedEventArgs e)
    {
        _controller.Config.EnableAdaptiveStorageOpacity = ChkAdaptiveStorage.IsChecked == true;
        _controller.SaveStorageAppearance();
    }

    private void StorageRange_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading || _syncingStorageRange) return;

        _syncingStorageRange = true;
        if (StorageMinSlider.Value > StorageMaxSlider.Value)
        {
            if (sender == StorageMinSlider) StorageMaxSlider.Value = StorageMinSlider.Value;
            else StorageMinSlider.Value = StorageMaxSlider.Value;
        }
        _syncingStorageRange = false;

        _controller.Config.StorageMinOpacity = StorageMinSlider.Value;
        _controller.Config.StorageMaxOpacity = StorageMaxSlider.Value;
        UpdateStorageRangeLabels();
        _controller.PreviewStorageOpacity(sender == StorageMinSlider ? StorageMinSlider.Value : StorageMaxSlider.Value);
        _storageSaveDelay.Stop();
        _storageSaveDelay.Start();
    }

    private void SavePendingStorageRange()
    {
        if (!_storageSaveDelay.IsEnabled) return;
        _storageSaveDelay.Stop();
        _controller.ConfigService.Save();
    }

    private void UpdateStorageRangeLabels()
    {
        StorageMinText.Text = $"{Math.Round(StorageMinSlider.Value * 100)}%";
        StorageMaxText.Text = $"{Math.Round(StorageMaxSlider.Value * 100)}%";
    }

    private void SystemOption_Changed(object sender, RoutedEventArgs e)
    {
        _controller.Config.AutoStart = ChkAutoStart.IsChecked == true;
        _controller.Config.AutoTidyDesktop = !_controller.Config.KeepDesktopFilesInPlace && ChkAutoTidy.IsChecked == true;
        bool hideIcons = ChkHideIcons.IsChecked == true;
        if (_controller.Config.HideDesktopIcons != hideIcons)
        {
            _controller.Config.HideDesktopIcons = hideIcons;
            DesktopIconsHelper.SetSystemIconsHidden(hideIcons);
            DesktopIconsHelper.SetDesktopIconsVisible(!hideIcons);
        }

        _controller.NotifyConfigUpdated();
    }

    private void DesktopMode_Changed(object sender, RoutedEventArgs e)
    {
        _controller.Config.KeepDesktopFilesInPlace = ChkKeepDesktopFiles.IsChecked == true;
        if (_controller.Config.KeepDesktopFilesInPlace) _controller.Config.AutoTidyDesktop = false;
        _controller.NotifyConfigUpdated();
    }

    private async void PinDesktopHub_Click(object sender, RoutedEventArgs e)
    {
        BtnPinDesktopHub.IsEnabled = false;
        QuickAccessStatus.Text = "正在请求 Windows 固定 DesktopHub…";
        try
        {
            var request = ShellFileOpsHelper.PinToQuickAccessAsync(_controller.Config.DesktopHubPath);
            bool submitted = await request.WaitAsync(TimeSpan.FromSeconds(6));
            QuickAccessStatus.Text = submitted
                ? "已向 Windows 提交固定请求，请在资源管理器的「快速访问」中确认。"
                : "未能固定。可在资源管理器中右键 DesktopHub，选择「固定到快速访问」。";
        }
        catch (TimeoutException)
        {
            QuickAccessStatus.Text = "Windows 暂未响应，请先查看「快速访问」，也可右键 DesktopHub 手动固定。";
        }
        finally
        {
            BtnPinDesktopHub.IsEnabled = true;
        }
    }

    private void WidgetToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkShowRecent == null || ChkShowMost == null) return;
        _controller.Config.ShowRecentWidget = ChkShowRecent.IsChecked == true;
        _controller.Config.ShowMostWidget = ChkShowMost.IsChecked == true;
        _controller.NotifyConfigUpdated();
    }

    private void TaskbarOption_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkTaskbarEnable == null || RbTaskbarClear == null) return;
        _controller.Config.EnableTaskbarTransparency = ChkTaskbarEnable.IsChecked == true;
        _controller.Config.TaskbarDarkOnMaximize = ChkTaskbarDarkMax.IsChecked == true;
        _controller.Config.TaskbarMode = RbTaskbarAcrylic.IsChecked == true
            ? "Acrylic"
            : (RbTaskbarTranslucent.IsChecked == true ? "Translucent" : "Clear");
        _controller.NotifyConfigUpdated();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        OpacityValueText.Text = $"{(int)(e.NewValue * 100)}%";
        _controller.Config.GlassOpacity = e.NewValue;
        _controller.NotifyConfigUpdated();
    }

    private void AccentPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string hex)
        {
            _controller.Config.AccentColorHex = hex;
            _controller.NotifyConfigUpdated();
        }
    }

    private void BtnTpl1_Click(object sender, RoutedEventArgs e) => LoadTemplateToGrid("1");
    private void BtnTpl2_Click(object sender, RoutedEventArgs e) => LoadTemplateToGrid("2");

    private void LoadTemplateToGrid(string tplId)
    {
        _categories.Clear();
        foreach (var c in ConfigService.GetTemplateCategories(tplId)) _categories.Add(c);
        _pendingTemplateId = tplId;
    }

    private void BtnAddCat_Click(object sender, RoutedEventArgs e)
    {
        int next = _categories.Count + 1;
        _categories.Add(new CategoryDef
        {
            FolderName = $"{next:00}_自定义分类",
            Title = "自定义分类",
            Subtitle = "点击编辑说明",
            AccentHex = "#A78BFA"
        });
    }

    private void BtnDelCat_Click(object sender, RoutedEventArgs e)
    {
        if (CategoriesGrid.SelectedItem is CategoryDef sel) _categories.Remove(sel);
    }

    private void BtnResetPos_Click(object sender, RoutedEventArgs e) => _resetPositionsAction();

    private void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var res = System.Windows.MessageBox.Show(
            "确定要将所有文件还原回原桌面位置并关闭美化吗？",
            "确认还原",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        int restored = _controller.BackupRestore.RestoreAll();
        _controller.Config.HideDesktopIcons = false;
        _controller.Config.AutoStart = false;
        _controller.Config.AutoTidyDesktop = false;
        _controller.Config.EnableTaskbarTransparency = false;
        _controller.NotifyConfigUpdated();
        DesktopIconsHelper.SetSystemIconsHidden(false);
        DesktopIconsHelper.SetDesktopIconsVisible(true);
        System.Windows.MessageBox.Show($"已成功还原 {restored} 个文件至原桌面，桌面图标已恢复显示。", "还原完成");
        System.Windows.Application.Current.Shutdown();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!CategoriesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true) ||
            !CategoriesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true)) return;
        _controller.Config.TemplateId = _pendingTemplateId;
        _controller.Config.Categories = _categories.ToList();

        _controller.NotifyConfigUpdated();
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
