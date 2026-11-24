using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using AuroraDesk.App.Helpers;
using AuroraDesk.Core.Models;
using AuroraDesk.Core.Services;
using AuroraDesk.Native;

namespace AuroraDesk.App.Windows;

public partial class WelcomeWizardWindow : Window
{
    private readonly AppController _controller;

    private sealed class PreviewRow
    {
        public string FileName { get; set; } = "";
        public string TargetCategory { get; set; } = "";
    }

    public WelcomeWizardWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        RefreshPreview();
    }

    private void Template_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        string tpl = RbTpl2.IsChecked == true ? "2" : "1";
        _controller.ConfigService.ApplyTemplate(tpl);
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var rows = new List<PreviewRow>();
        if (Directory.Exists(userDesktop))
        {
            foreach (var entry in Directory.GetFileSystemEntries(userDesktop))
            {
                string name = Path.GetFileName(entry);
                if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                rows.Add(new PreviewRow
                {
                    FileName = name,
                    TargetCategory = _controller.Classifier.ClassifyItem(entry, _controller.Config)
                });
            }
        }

        PreviewHeader.Text = rows.Count > 0
            ? $"2. 即将归档以下 {rows.Count} 个桌面文件（所有文件均保留在用户目录 DesktopHub，可随时一键还原）："
            : "2. 检测到桌面已保持整洁（或已存在 DesktopHub），将直接启用当前分类！";

        PreviewGrid.ItemsSource = rows;
    }

    private void BtnSkipTidy_Click(object sender, RoutedEventArgs e)
    {
        FinishWizard(tidyDesktop: false);
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        FinishWizard(tidyDesktop: true);
    }

    private void FinishWizard(bool tidyDesktop)
    {
        string tpl = RbTpl2.IsChecked == true ? "2" : "1";
        // Only overwrite categories if user didn't already have custom DesktopHub layout or explicitly chose template
        if (!Directory.Exists(_controller.Config.DesktopHubPath) || Directory.GetDirectories(_controller.Config.DesktopHubPath).Length < 4 || RbTpl2.IsChecked == true)
        {
            _controller.Config.TemplateId = tpl;
            _controller.Config.Categories = ConfigService.GetTemplateCategories(tpl);
        }

        _controller.Config.Initialized = true;
        _controller.ConfigService.EnsureCategoryFolders();

        if (tidyDesktop)
        {
            _controller.BackupRestore.TidyDesktop(_controller.Config, _controller.Classifier);
        }

        DesktopIconsHelper.SetSystemIconsHidden(_controller.Config.HideDesktopIcons);
        DesktopIconsHelper.SetDesktopIconsVisible(!_controller.Config.HideDesktopIcons);
        _controller.ApplyAutostart(_controller.Config.AutoStart);
        _controller.NotifyConfigUpdated();
        DialogResult = true;
        Close();
    }
}
