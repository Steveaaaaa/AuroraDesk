using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using AuroraDesk.Core.Models;
using AuroraDesk.Core.Services;
using AuroraDesk.Native;
using Microsoft.Win32;

namespace AuroraDesk.App.Helpers;

public sealed class AppController
{
    public string BaseDir { get; }
    public string DataDir { get; }
    public string IconCacheDir { get; }
    public string BackupDir { get; }

    public ConfigService ConfigService { get; }
    public UsageStatsService UsageStats { get; }
    public ClassifierService Classifier { get; }
    public SearchService Search { get; }
    public BackupRestoreService BackupRestore { get; }
    public DesktopCategoryIndex DesktopCategories { get; }

    public AppConfig Config => ConfigService.Current;

    public event Action? ConfigChanged;
    public event Action? DesktopItemsChanged;
    public event Action<double?>? StorageAppearanceChanged;
    public event Action<string, string, bool>? OpenDrawerRequested;
    public event Action<string>? CloseDrawerIfMatchesRequested;
    public event Action? OpenSettingsRequested;
    public event Action? OpenSearchRequested;

    private FileSystemWatcher? _desktopWatcher;
    private System.Threading.Timer? _watcherDebounce;
    private readonly string _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public AppController()
    {
        BaseDir = AppContext.BaseDirectory;
        DataDir = Path.Combine(BaseDir, "Data");
        IconCacheDir = Path.Combine(DataDir, "Icons");
        BackupDir = Path.Combine(BaseDir, "Backup");

        string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuroraDesk");
        ConfigService = new ConfigService(configDir, DataDir);
        UsageStats = new UsageStatsService(DataDir);
        Classifier = new ClassifierService();
        DesktopCategories = new DesktopCategoryIndex(_desktopPath, Classifier);
        Search = new SearchService();
        BackupRestore = new BackupRestoreService(BackupDir);

        ConfigService.EnsureCategoryFolders();
        ExtractSystemIconsAsync();
        RefreshDesktopIndex();
    }

    private void ExtractSystemIconsAsync()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloads = Path.Combine(userHome, "Downloads");
            ShellIconHelper.GetOrCacheIconPng("shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", IconCacheDir, "sys_pc.png");
            ShellIconHelper.GetOrCacheIconPng("shell:::{645FF040-5081-101B-9F08-00AA002F954E}", IconCacheDir, "sys_trash.png");
            ShellIconHelper.GetOrCacheIconPng(userHome, IconCacheDir, "sys_home.png");
            ShellIconHelper.GetOrCacheIconPng(downloads, IconCacheDir, "sys_down.png");
            ShellIconHelper.GetOrCacheIconPng(@"C:\Windows\System32\SystemSettingsAdminFlows.exe", IconCacheDir, "sys_settings.png");
            ShellIconHelper.GetOrCacheIconPng(@"C:\Windows\System32\Taskmgr.exe", IconCacheDir, "sys_taskmgr.png");
        });
    }

    public void RequestDrawer(string folderPath, string title, bool toggle = false) => OpenDrawerRequested?.Invoke(folderPath, title, toggle);
    public void RequestCloseDrawerIfMatches(string folderPath) => CloseDrawerIfMatchesRequested?.Invoke(folderPath);
    public void RequestSettings() => OpenSettingsRequested?.Invoke();
    public void RequestSearch() => OpenSearchRequested?.Invoke();

    public void NotifyConfigUpdated()
    {
        ConfigService.EnsureCategoryFolders();
        ConfigService.Save();
        ApplyAutostart(Config.AutoStart);
        UpdateDesktopWatcher();
        RefreshDesktopIndex();
        ConfigChanged?.Invoke();
    }

    private void RefreshDesktopIndex()
    {
        DesktopCategories.Refresh(Config);
        Search.RebuildIndex(Config, DesktopCategories);
    }

    public void SaveStorageAppearance()
    {
        ConfigService.Save();
        StorageAppearanceChanged?.Invoke(null);
    }

    public void PreviewStorageOpacity(double opacity) => StorageAppearanceChanged?.Invoke(opacity);

    public void LaunchTarget(string path, bool asAdmin = false, bool trackUsage = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        if (path == "auroradesk://settings") { RequestSettings(); return; }
        if (path == "auroradesk://tidy") { RunTidyDesktop(showNotice: true); return; }
        if (path.StartsWith("calc://", StringComparison.Ordinal))
        {
            string val = path["calc://".Length..];
            System.Windows.Clipboard.SetText(val);
            return;
        }

        if (Directory.Exists(path))
        {
            if (path.StartsWith(Config.DesktopHubPath, StringComparison.OrdinalIgnoreCase))
            {
                string title = Path.GetFileName(path.TrimEnd('\\'));
                RequestDrawer(path, title);
                return;
            }
            OpenInExplorer(path);
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };
            if (!path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            {
                psi.WorkingDirectory = Path.GetDirectoryName(path) ?? "";
            }
            if (asAdmin) psi.Verb = "runas";
            Process.Start(psi);

            if (trackUsage && File.Exists(path))
            {
                ShellIconHelper.GetOrCacheIconPng(path, IconCacheDir);
                UsageStats.RecordLaunch(path);
            }
        }
        catch { }
    }

    public static void OpenInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
        }
        catch { }
    }

    public int RunTidyDesktop(bool showNotice)
    {
        bool keepInPlace = Config.KeepDesktopFilesInPlace;
        int count = keepInPlace ? 0 : BackupRestore.TidyDesktop(Config, Classifier);
        if (keepInPlace || count > 0)
        {
            RefreshDesktopIndex();
            DesktopItemsChanged?.Invoke();
        }
        if (keepInPlace) count = DesktopCategories.Count;
        if (showNotice)
        {
            string message = keepInPlace
                ? $"已刷新 {count} 个桌面项目的分类，文件仍在原处。"
                : count > 0 ? $"已智能归档 {count} 个桌面新文件至收纳工作台！" : "当前桌面已经干干净净，没有待整理的新文件。";
            System.Windows.MessageBox.Show(
                message,
                "AuroraDesk · 智能整理",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        return count;
    }

    public void ApplyAutostart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key == null) return;
            string? exe = Environment.ProcessPath;
            if (enable && !string.IsNullOrEmpty(exe))
            {
                key.SetValue("AuroraDesk", $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue("AuroraDesk", false);
            }
        }
        catch { }
    }

    public void UpdateDesktopWatcher()
    {
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _watcherDebounce?.Change(Timeout.Infinite, Timeout.Infinite);

        if ((!Config.AutoTidyDesktop && !Config.KeepDesktopFilesInPlace) || !Directory.Exists(_desktopPath)) return;

        _desktopWatcher = new FileSystemWatcher(_desktopPath)
        {
            IncludeSubdirectories = false
        };
        _desktopWatcher.Created += (_, _) => ScheduleAutoTidy();
        _desktopWatcher.Renamed += (_, _) => ScheduleAutoTidy();
        if (Config.KeepDesktopFilesInPlace) _desktopWatcher.Deleted += (_, _) => ScheduleAutoTidy();
        _desktopWatcher.EnableRaisingEvents = true;
    }

    private void ScheduleAutoTidy()
    {
        _watcherDebounce ??= new System.Threading.Timer(_ =>
        {
            _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Config.KeepDesktopFilesInPlace || Config.AutoTidyDesktop) RunTidyDesktop(showNotice: false);
            }));
        }, null, 2500, Timeout.Infinite);
        _watcherDebounce.Change(2500, Timeout.Infinite);
    }

    public void ShutdownCleanup()
    {
        _desktopWatcher?.Dispose();
        _watcherDebounce?.Dispose();
        UsageStats.Flush();
        ConfigService.Save();
    }
}
