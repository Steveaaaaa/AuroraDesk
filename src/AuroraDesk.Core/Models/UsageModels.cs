using System.Collections.Generic;

namespace AuroraDesk.Core.Models;

public sealed class UsageEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public long LastUsedTicks { get; set; }
    public bool IsPinned { get; set; }
}

public sealed class UsageStore
{
    public List<UsageEntry> Items { get; set; } = [];
}

public sealed class MoveHistoryItem
{
    public string OriginalPath { get; set; } = "";
    public string MovedToPath { get; set; } = "";
    public string Timestamp { get; set; } = "";
}

public sealed class SearchResultItem
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string PinyinInitials { get; set; } = "";
    public string Kind { get; set; } = "App"; // App, File, System, Calc
    public int Score { get; set; }
}
