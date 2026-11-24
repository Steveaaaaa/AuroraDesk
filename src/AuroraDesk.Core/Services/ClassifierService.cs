using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AuroraDesk.Core.Models;
using AuroraDesk.Native;

namespace AuroraDesk.Core.Services;

public sealed class ClassifierService
{
    private static readonly Regex ReEngMech = new(
        @"AutoCAD|acad|SolidWorks|SLDWORKS|CATIA|Creo|ProE|Inventor|Revit|SketchUp|Rhino|PKPM|天正|TArch|广联达|CAXA|Mastercam|3ds\s*Max|Blender|ANSYS|Fluent|Workbench|Abaqus|COMSOL|Adams|Simpack|MATLAB|Simulink|Origin|Mathematica|Maple|SPSS|Minitab|Aspen|ChemDraw|VASP|Materials\s*Studio",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReEngElec = new(
        @"Keil|uVision|MDK|STM32|CubeMX|Proteus|Multisim|Altium|OrCAD|Cadence|Allegro|KiCad|立创|LCEDA|Quartus|Vivado|Xilinx|Modelsim|LabVIEW|TIA\s*Portal|博途|GX\s*Works|PLC|XCOM|SSCOM|串口|MobaXterm|SecureCRT|Xshell|Putty|WinSCP|J-Flash|ST-LINK|Saleae",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReDev = new(
        @"Code\.exe|VS\s*Code|Visual\s*Studio|IntelliJ|PyCharm|CLion|WebStorm|GoLand|DataGrip|Android\s*Studio|Eclipse|Dev-C\+\+|CodeBlocks|Anaconda|Miniconda|Jupyter|Spyder|Docker|VMware|VirtualBox|Git|Postman|Apifox|Apipost|DBeaver|Navicat|SQLyog|MySQL|Redis|FinalShell|DeepSeek|Gemini|ChatGPT|Claude|Kimi|Cursor|Antigravity|Node\.js|Python|LightC|Switch|Harness|WorkBuddy|牛客",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReSciDoc = new(
        @"Zotero|EndNote|NoteExpress|CAJViewer|知网|Overleaf|TeXstudio|MikTeX|LaTeX|Visio|Draw\.io|XMind|Obsidian|Typora|Notion|简历|面经|论文|毕设|课程|实验|报告|讲义|复习|考研|笔记|学习",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReSocial = new(
        @"QQ|微信|WeChat|Weixin|TIM|钉钉|DingTalk|飞书|Feishu|腾讯会议|WeMeeting|Zoom|Discord|Chrome|Edge|Firefox|浏览器|夸克|百度网盘|阿里云盘|OneDrive|音乐|Music|Spotify|网易云|视频|Bilibili|哔哩哔哩|抖音|爱奇艺|Clash|v2ray|FxSound|猫猫云|随云",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReGame = new(
        @"Steam|WeGame|Epic|Ubisoft|Riot|Battle\.net|原神|鸣潮|星穹|绝区零|英雄联盟|League|Valorant|无畏契约|CS2|CSGO|Minecraft|PCL|ModOrganizer|Apex|PUBG|永劫|三角洲|黑神话|加速器|雷神|UU|AK|MuMu|雷电|游戏|Slay|Skyrim|Witcher|Cyberpunk|Paradox|燕云|王者",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReTools = new(
        @"卡巴斯基|火绒|360|电脑管家|图吧|DiskGenius|Bandizip|7-Zip|WinRAR|Everything|Geek|Uninstall|NVIDIA|AMD|RTX|驱动|ToDesk|向日葵|Sunlogin",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string ClassifyItem(string fullPath, AppConfig config)
    {
        string name = Path.GetFileName(fullPath);
        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        string targetInfo = "";
        if (ext == ".lnk")
        {
            var (t, d) = ShortcutResolver.ResolveLnk(fullPath);
            targetInfo = $"{t} {d}";
        }
        string probe = $"{name} {targetInfo}";

        // 1. Check user custom category keywords & extensions first
        foreach (var cat in config.Categories)
        {
            if (!string.IsNullOrWhiteSpace(cat.Keywords))
            {
                var kws = cat.Keywords.Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (kws.Any(k => probe.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return cat.FolderName;
                }
            }
            if (!string.IsNullOrWhiteSpace(cat.Extensions) && !string.IsNullOrEmpty(ext))
            {
                var exts = cat.Extensions.Split([',', '，', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (exts.Any(e => string.Equals(e.StartsWith('.') ? e : "." + e, ext, StringComparison.OrdinalIgnoreCase)))
                {
                    return cat.FolderName;
                }
            }
        }

        // 2. Smart built-in engineering & student classifier matched against current category titles/folders
        string? FindCat(params string[] keywords) =>
            config.Categories.FirstOrDefault(c => keywords.Any(k => c.FolderName.Contains(k) || c.Title.Contains(k)))?.FolderName;

        if (ReDev.IsMatch(probe))
            return FindCat("开发", "IDE", "编程", "AI") ?? FallbackFolder(config);

        if (ReEngMech.IsMatch(probe) || ReEngElec.IsMatch(probe))
            return FindCat("工科", "仿真", "AI") ?? FallbackFolder(config);

        if (ReSocial.IsMatch(probe))
            return FindCat("社交", "影音", "浏览") ?? FallbackFolder(config);

        if (ext == ".url" || ReGame.IsMatch(probe))
            return FindCat("游戏", "娱乐", "大作") ?? FallbackFolder(config);

        if (ReSciDoc.IsMatch(probe))
            return FindCat("论文", "学习", "简历") ?? FallbackFolder(config);

        if (ReTools.IsMatch(probe) || ext is ".exe" or ".msi" or ".zip" or ".7z" or ".rar" or ".iso")
            return FindCat("工具", "安装包", "安全") ?? FallbackFolder(config);

        if (ext is ".doc" or ".docx" or ".xls" or ".xlsx" or ".csv")
            return FindCat("办公", "表格", "日常", "个人") ?? FallbackFolder(config);

        if (ext is ".pdf" or ".ppt" or ".pptx" or ".md")
            return FindCat("论文", "学习", "简历") ?? FallbackFolder(config);

        if (ext is ".txt" or ".jpg" or ".jpeg" or ".png" or ".bmp")
            return FindCat("个人", "生活", "日常") ?? FallbackFolder(config);

        return FallbackFolder(config);
    }

    private static string FallbackFolder(AppConfig config)
    {
        return config.Categories.FirstOrDefault(c => c.FolderName.Contains("其他") || c.FolderName.Contains("待整理"))?.FolderName
            ?? config.Categories.LastOrDefault()?.FolderName
            ?? "08_其他与待整理";
    }
}
