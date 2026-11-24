using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AuroraDesk.Core.Helpers;
using AuroraDesk.Core.Models;

namespace AuroraDesk.Core.Services;

public sealed class SearchService
{
    private List<SearchResultItem> _index = [];
    private readonly object _lock = new();

    public void RebuildIndex(AppConfig config, DesktopCategoryIndex? desktop = null)
    {
        var items = new List<SearchResultItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Index DesktopHub items (highest priority!)
        if (Directory.Exists(config.DesktopHubPath))
        {
            foreach (var catDir in Directory.GetDirectories(config.DesktopHubPath))
            {
                string catLabel = Path.GetFileName(catDir);
                foreach (var entry in Directory.GetFileSystemEntries(catDir))
                {
                    string name = UsageStatsService.CleanDisplayName(entry);
                    if (seenPaths.Add(entry))
                    {
                        items.Add(new SearchResultItem
                        {
                            Title = name,
                            Subtitle = $"收纳箱 · {catLabel}",
                            TargetPath = entry,
                            PinyinInitials = PinyinHelper.GetInitials(name),
                            Kind = Directory.Exists(entry) ? "Folder" : "HubItem"
                        });
                    }
                }
            }
        }

        if (config.KeepDesktopFilesInPlace && desktop != null)
        {
            foreach (var category in config.Categories)
            {
                foreach (var path in desktop.GetEntries(Path.Combine(config.DesktopHubPath, category.FolderName)))
                {
                    if (!seenPaths.Add(path)) continue;
                    string name = UsageStatsService.CleanDisplayName(path);
                    items.Add(new SearchResultItem
                    {
                        Title = name,
                        Subtitle = $"桌面原件 · {category.Title}",
                        TargetPath = path,
                        PinyinInitials = PinyinHelper.GetInitials(name),
                        Kind = Directory.Exists(path) ? "Folder" : "HubItem"
                    });
                }
            }
        }

        // 2. Index Start Menu shortcuts
        string[] startDirs =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        ];
        foreach (var root in startDirs)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string name = UsageStatsService.CleanDisplayName(file);
                    if (name.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("卸载")) continue;
                    if (seenPaths.Add(file))
                    {
                        items.Add(new SearchResultItem
                        {
                            Title = name,
                            Subtitle = "开始菜单应用",
                            TargetPath = file,
                            PinyinInitials = PinyinHelper.GetInitials(name),
                            Kind = "App"
                        });
                    }
                }
            }
            catch { }
        }

        // 3. Built-in quick system commands
        (string title, string sub, string target)[] sysCommands =
        [
            ("此电脑", "系统入口", "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
            ("回收站", "系统入口", "shell:::{645FF040-5081-101B-9F08-00AA002F954E}"),
            ("系统设置", "Windows 设置", "ms-settings:"),
            ("任务管理器", "系统工具", "taskmgr.exe"),
            ("控制面板", "系统工具", "control.exe"),
            ("计算器", "系统工具", "calc.exe"),
            ("命令行终端", "Windows Terminal / CMD", "cmd.exe"),
            ("AuroraDesk 设置中心", "打开美学配置", "auroradesk://settings"),
            ("整理桌面新文件", "一键归类桌面文件", "auroradesk://tidy")
        ];
        foreach (var (t, s, target) in sysCommands)
        {
            items.Add(new SearchResultItem
            {
                Title = t,
                Subtitle = s,
                TargetPath = target,
                PinyinInitials = PinyinHelper.GetInitials(t),
                Kind = "System"
            });
        }

        lock (_lock) { _index = items; }
    }

    public List<SearchResultItem> Query(string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var results = new List<SearchResultItem>();

        if (MathEvaluator.TryEvaluate(query, out double calcVal))
        {
            string valStr = Math.Abs(calcVal - Math.Round(calcVal)) < 1e-10
                ? Math.Round(calcVal).ToString("0")
                : calcVal.ToString("0.######");
            results.Add(new SearchResultItem
            {
                Title = $"= {valStr}",
                Subtitle = $"计算结果（回车复制 {valStr} 到剪贴板）",
                TargetPath = $"calc://{valStr}",
                Kind = "Calc",
                Score = 200
            });
        }

        List<SearchResultItem> snapshot;
        lock (_lock) { snapshot = _index; }

        var matched = new List<SearchResultItem>();
        foreach (var item in snapshot)
        {
            int score = PinyinHelper.MatchScore(query, item.Title, item.PinyinInitials);
            if (score > 0)
            {
                if (item.Kind == "HubItem") score += 10; // Slight boost to user's curated DesktopHub items
                matched.Add(new SearchResultItem
                {
                    Title = item.Title,
                    Subtitle = item.Subtitle,
                    TargetPath = item.TargetPath,
                    PinyinInitials = item.PinyinInitials,
                    Kind = item.Kind,
                    Score = score
                });
            }
        }

        results.AddRange(matched.OrderByDescending(x => x.Score).ThenBy(x => x.Title.Length).Take(limit));
        return results;
    }
}
