using System;
using System.Collections.Generic;

namespace AuroraDesk.Core.Models;

public sealed class WindowPos
{
    public double Left { get; set; }
    public double Top { get; set; }
    public bool HasCustomPosition { get; set; }
}

public sealed class CategoryDef
{
    public string FolderName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string AccentHex { get; set; } = "#A78BFA";
    public string Keywords { get; set; } = "";
    public string Extensions { get; set; } = "";
}

public sealed class AppConfig
{
    public const double StorageOpacityFloor = 0.30;
    public const double StorageOpacityCeiling = 0.88;

    public bool Initialized { get; set; }
    public string TemplateId { get; set; } = "1";
    public string DesktopHubPath { get; set; } = "";
    public double GlassOpacity { get; set; } = 0.38;
    public bool EnableAdaptiveStorageOpacity { get; set; }
    public double StorageMinOpacity { get; set; } = 0.48;
    public double StorageMaxOpacity { get; set; } = 0.82;
    public string AccentColorHex { get; set; } = "#A78BFA";
    public bool AutoTidyDesktop { get; set; }
    public bool KeepDesktopFilesInPlace { get; set; }
    public bool AutoStart { get; set; } = true;
    public bool HideDesktopIcons { get; set; } = true;
    public bool ShowRecentWidget { get; set; } = true;
    public bool ShowMostWidget { get; set; } = true;
    public bool EnableTaskbarTransparency { get; set; } = true;
    public string TaskbarMode { get; set; } = "Clear";
    public bool TaskbarDarkOnMaximize { get; set; } = true;
    public int RecentSlots { get; set; } = 8;
    public int MostSlots { get; set; } = 8;

    public WindowPos LeftSystemPos { get; set; } = new() { Left = 28, Top = 48 };
    public WindowPos LeftRecentPos { get; set; } = new() { Left = 28, Top = 218 };
    public WindowPos LeftMostPos { get; set; } = new() { Left = 28, Top = 410 };
    public WindowPos RightHubPos { get; set; } = new() { Left = 1624, Top = 48 };

    public List<CategoryDef> Categories { get; set; } = [];

    public (double Min, double Max) GetStorageOpacityRange()
    {
        double min = Math.Clamp(StorageMinOpacity, StorageOpacityFloor, StorageOpacityCeiling);
        return (min, Math.Clamp(StorageMaxOpacity, min, StorageOpacityCeiling));
    }
}
