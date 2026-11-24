using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AuroraDesk.Core.Models;

namespace AuroraDesk.Core.Services;

public sealed class UsageStatsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _statsPath;
    private readonly object _lock = new();
    private UsageStore _store;
    private long _latestUseTicks;
    private Timer? _saveTimer;

    public event Action? StatsChanged;

    public UsageStatsService(string dataDir)
    {
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        _statsPath = Path.Combine(dataDir, "usage_stats.json");
        _store = LoadStore();
        _latestUseTicks = _store.Items.Select(item => item.LastUsedTicks).DefaultIfEmpty().Max();
    }

    public void RecordLaunch(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath)) return;

        RecordUse(targetPath, countLaunch: true);
        StatsChanged?.Invoke();
    }

    public void RecordFolderOpen(string path)
    {
        if (Directory.Exists(path)) RecordUse(path, countLaunch: false);
    }

    private void RecordUse(string path, bool countLaunch)
    {
        lock (_lock)
        {
            var entry = _store.Items.FirstOrDefault(item =>
                string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new UsageEntry { Path = path };
                _store.Items.Add(entry);
            }
            entry.Name = CleanDisplayName(path);
            if (countLaunch) entry.Count++;
            _latestUseTicks = Math.Max(DateTime.Now.Ticks, _latestUseTicks + 1);
            entry.LastUsedTicks = _latestUseTicks;
            ScheduleSave();
        }
    }

    public List<string> SortDrawerEntries(IEnumerable<string> paths)
    {
        Dictionary<string, long> lastUsed = new(StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            foreach (var item in _store.Items)
                lastUsed[item.Path] = Math.Max(lastUsed.GetValueOrDefault(item.Path), item.LastUsedTicks);
        }

        return paths.OrderByDescending(path => lastUsed.GetValueOrDefault(path))
            .ThenByDescending(Directory.Exists)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public List<UsageEntry> GetRecent(int maxCount)
    {
        lock (_lock)
        {
            return _store.Items
                .Where(x => File.Exists(x.Path) && x.LastUsedTicks > 0)
                .OrderByDescending(x => x.LastUsedTicks)
                .Take(maxCount)
                .Select(CloneEntry)
                .ToList();
        }
    }

    public List<UsageEntry> GetMostUsed(int maxCount)
    {
        lock (_lock)
        {
            return _store.Items
                .Where(x => File.Exists(x.Path) && (x.Count > 0 || x.IsPinned))
                .OrderByDescending(x => x.IsPinned)
                .ThenByDescending(x => x.Count)
                .ThenByDescending(x => x.LastUsedTicks)
                .Take(maxCount)
                .Select(CloneEntry)
                .ToList();
        }
    }

    public void RemoveFromRecent(string path)
    {
        lock (_lock)
        {
            var item = _store.Items.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.LastUsedTicks = 0;
                if (item.Count == 0 && !item.IsPinned) _store.Items.Remove(item);
                ScheduleSave();
            }
        }
        StatsChanged?.Invoke();
    }

    public void TogglePin(string path)
    {
        lock (_lock)
        {
            var item = _store.Items.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.IsPinned = !item.IsPinned;
                ScheduleSave();
            }
        }
        StatsChanged?.Invoke();
    }

    public void ResetItemCount(string path)
    {
        lock (_lock)
        {
            var item = _store.Items.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.Count = 0;
                item.IsPinned = false;
                if (item.LastUsedTicks == 0) _store.Items.Remove(item);
                ScheduleSave();
            }
        }
        StatsChanged?.Invoke();
    }

    public void UpdateItemPath(string oldPath, string newPath)
    {
        bool changed = false;
        lock (_lock)
        {
            var item = _store.Items.FirstOrDefault(x => string.Equals(x.Path, oldPath, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.Path = newPath;
                item.Name = CleanDisplayName(newPath);
                changed = true;
                ScheduleSave();
            }
        }
        if (changed) StatsChanged?.Invoke();
    }

    public void UpdateFolderPrefix(string oldFolder, string newFolder)
    {
        string oldPrefix = oldFolder.TrimEnd('\\') + "\\";
        string newPrefix = newFolder.TrimEnd('\\') + "\\";
        bool changed = false;
        lock (_lock)
        {
            foreach (var item in _store.Items)
            {
                if (item.Path.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    item.Path = newPrefix + item.Path[oldPrefix.Length..];
                    changed = true;
                }
            }
            if (changed) ScheduleSave();
        }
        if (changed) StatsChanged?.Invoke();
    }

    public void Flush()
    {
        lock (_lock)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
            SaveImmediate();
        }
    }

    private void ScheduleSave()
    {
        _saveTimer ??= new Timer(_ =>
        {
            lock (_lock) { SaveImmediate(); }
        }, null, 500, Timeout.Infinite);
        _saveTimer.Change(500, Timeout.Infinite);
    }

    private void SaveImmediate()
    {
        try
        {
            File.WriteAllText(_statsPath, JsonSerializer.Serialize(_store, JsonOpts));
        }
        catch { }
    }

    private UsageStore LoadStore()
    {
        if (!File.Exists(_statsPath)) return new UsageStore();
        try
        {
            return JsonSerializer.Deserialize<UsageStore>(File.ReadAllText(_statsPath)) ?? new UsageStore();
        }
        catch
        {
            return new UsageStore();
        }
    }

    public static string CleanDisplayName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) name = Path.GetFileName(path);
        return name.Replace(" - 快捷方式", "").Replace(" - Shortcut", "").Trim();
    }

    private static UsageEntry CloneEntry(UsageEntry x) => new()
    {
        Path = x.Path,
        Name = x.Name,
        Count = x.Count,
        LastUsedTicks = x.LastUsedTicks,
        IsPinned = x.IsPinned
    };
}
