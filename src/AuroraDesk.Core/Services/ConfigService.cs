using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AuroraDesk.Core.Models;

namespace AuroraDesk.Core.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _configPath;
    private readonly string _backupPath;
    private bool _keepBackup;

    public AppConfig Current { get; }

    public ConfigService(string dataDir, string? legacyDataDir = null)
    {
        Directory.CreateDirectory(dataDir);
        _configPath = Path.Combine(dataDir, "config.json");
        _backupPath = _configPath + ".bak";

        var current = ReadConfig(_configPath);
        var recovered = current == null ? ReadConfig(_backupPath) : null;
        var legacy = current == null && recovered == null && legacyDataDir != null
            ? ReadConfig(Path.Combine(legacyDataDir, "config.json")) : null;
        _keepBackup = current == null;
        Current = current ?? recovered ?? legacy ?? CreateDefault();
        if (string.IsNullOrWhiteSpace(Current.DesktopHubPath))
            Current.DesktopHubPath = DefaultHubPath;
        if (current == null && (recovered != null || legacy != null))
            Save();
    }

    public void Save()
    {
        var temporaryPath = _configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, Current, JsonOpts);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_configPath))
                File.Replace(temporaryPath, _configPath, _keepBackup ? null : _backupPath);
            else
                File.Move(temporaryPath, _configPath);
            _keepBackup = false;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public void ApplyTemplate(string templateId)
    {
        Current.TemplateId = templateId;
        Current.Categories = GetTemplateCategories(templateId);
        EnsureCategoryFolders();
        Save();
    }

    public void EnsureCategoryFolders()
    {
        if (string.IsNullOrWhiteSpace(Current.DesktopHubPath))
        {
            Current.DesktopHubPath = DefaultHubPath;
        }
        if (!Directory.Exists(Current.DesktopHubPath))
        {
            Directory.CreateDirectory(Current.DesktopHubPath);
        }
        foreach (var cat in Current.Categories)
        {
            var dir = Path.Combine(Current.DesktopHubPath, cat.FolderName);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }

    private static AppConfig? ReadConfig(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var config = JsonSerializer.Deserialize<AppConfig>(stream);
            return config?.Categories != null &&
                config.LeftSystemPos != null && config.LeftRecentPos != null &&
                config.LeftMostPos != null && config.RightHubPos != null ? config : null;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { return null; }
    }

    private static string DefaultHubPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DesktopHub");

    private static AppConfig CreateDefault() => new()
    {
        DesktopHubPath = DefaultHubPath,
        Categories = DetectExistingOrDefault(DefaultHubPath)
    };

    private static List<CategoryDef> DetectExistingOrDefault(string hubPath)
    {
        // If user already has the 8-folder AuroraDesk layout in DesktopHub, inherit them cleanly!
        if (Directory.Exists(hubPath))
        {
            var dirs = Directory.GetDirectories(hubPath);
            if (dirs.Length >= 4)
            {
                string[] palette = ["#38BDF8", "#A78BFA", "#F472B6", "#34D399", "#FBBF24", "#E879F9", "#FB923C", "#94A3B8"];
                var detected = new List<CategoryDef>();
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string folder = Path.GetFileName(dirs[i]);
                    string title = folder.Length > 3 && char.IsDigit(folder[0]) && char.IsDigit(folder[1]) && folder[2] == '_'
                        ? folder[3..]
                        : folder;
                    int count = Directory.GetFileSystemEntries(dirs[i]).Length;
                    detected.Add(new CategoryDef
                    {
                        FolderName = folder,
                        Title = title,
                        Subtitle = $"已收录 {count} 个项目",
                        AccentHex = palette[i % palette.Length]
                    });
                }
                return detected;
            }
        }
        return GetTemplateCategories("1");
    }

    public static List<CategoryDef> GetTemplateCategories(string templateId) => templateId switch
    {
        "2" =>
        [
            new() { FolderName = "01_开发工具与IDE",   Title = "开发工具与 IDE",   Subtitle = "VSCode · IDEA · Docker · Git", AccentHex = "#38BDF8", Extensions = ".py,.java,.js,.ts,.c,.cpp,.go,.rs,.sql,.sh,.ps1,.bat" },
            new() { FolderName = "02_工科软件与AI",     Title = "工科软件与 AI",    Subtitle = "MATLAB · CAD · Keil · EDA",    AccentHex = "#A78BFA", Extensions = ".m,.slx,.dwg" },
            new() { FolderName = "03_社交影音与浏览",   Title = "社交影音与浏览",   Subtitle = "QQ · 微信 · 浏览器 · 音乐",    AccentHex = "#E879F9" },
            new() { FolderName = "04_游戏与娱乐休闲",   Title = "游戏与娱乐休闲",   Subtitle = "Steam · WeGame · 游戏大作",    AccentHex = "#F472B6", Extensions = ".url" },
            new() { FolderName = "05_简历面试与论文",   Title = "简历面试与论文",   Subtitle = "简历 · 面经 · 论文 · 笔记",    AccentHex = "#FBBF24", Extensions = ".pdf,.ppt,.pptx,.md" },
            new() { FolderName = "06_个人与日常文档",   Title = "个人与日常文档",   Subtitle = "日常文档 · 图片 · 表格",       AccentHex = "#FB923C", Extensions = ".doc,.docx,.xls,.xlsx,.csv,.txt,.jpg,.png" },
            new() { FolderName = "07_安装包与系统工具", Title = "安装包与系统工具", Subtitle = "驱动 · 压缩包 · 硬件工具箱",   AccentHex = "#34D399", Extensions = ".exe,.msi,.zip,.7z,.rar,.iso" },
            new() { FolderName = "08_其他与待整理",     Title = "其他与待整理",     Subtitle = "冷门快捷方式 · 临时暂存",      AccentHex = "#94A3B8" }
        ],
        _ =>
        [
            new() { FolderName = "01_课程学习与论文",   Title = "课程学习与论文",   Subtitle = "PPT · PDF · 论文 · 笔记",      AccentHex = "#38BDF8", Extensions = ".pdf,.ppt,.pptx,.md" },
            new() { FolderName = "02_工科仿真与编程",   Title = "工科仿真与编程",   Subtitle = "MATLAB · CAD · Keil · Code",   AccentHex = "#A78BFA", Extensions = ".py,.java,.c,.cpp,.m,.slx,.dwg" },
            new() { FolderName = "03_常用办公与表格",   Title = "常用办公与表格",   Subtitle = "Word · Excel · WPS · 表格",    AccentHex = "#34D399", Extensions = ".doc,.docx,.xls,.xlsx,.csv" },
            new() { FolderName = "04_社交影音与浏览",   Title = "社交影音与浏览",   Subtitle = "微信 · QQ · 浏览器 · 音乐",    AccentHex = "#E879F9" },
            new() { FolderName = "05_游戏与娱乐休闲",   Title = "游戏与娱乐休闲",   Subtitle = "Steam · WeGame · 游戏大作",    AccentHex = "#F472B6", Extensions = ".url" },
            new() { FolderName = "06_个人与生活文档",   Title = "个人与生活文档",   Subtitle = "照片 · 个人资料 · 文本",       AccentHex = "#FBBF24", Extensions = ".txt,.jpg,.jpeg,.png,.bmp" },
            new() { FolderName = "07_安装包与系统工具", Title = "安装包与系统工具", Subtitle = "安装包 · 驱动 · 杀软工具",     AccentHex = "#FB923C", Extensions = ".exe,.msi,.zip,.7z,.rar,.iso" },
            new() { FolderName = "08_其他与待整理",     Title = "其他与待整理",     Subtitle = "冷门快捷方式 · 临时暂存",      AccentHex = "#94A3B8" }
        ]
    };
}
