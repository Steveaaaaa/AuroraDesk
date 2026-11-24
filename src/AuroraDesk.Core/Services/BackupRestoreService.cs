using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AuroraDesk.Core.Models;

namespace AuroraDesk.Core.Services;

public sealed class BackupRestoreService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _historyPath;

    public BackupRestoreService(string backupDir)
    {
        if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
        _historyPath = Path.Combine(backupDir, "move_history.json");
    }

    public int TidyDesktop(AppConfig config, ClassifierService classifier, bool includePublicDesktop = false)
    {
        if (config.KeepDesktopFilesInPlace) return 0;

        var sources = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
        if (includePublicDesktop) sources.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

        var history = LoadHistory();
        int moved = 0;

        foreach (var srcDir in sources)
        {
            if (!Directory.Exists(srcDir)) continue;
            foreach (var entry in Directory.GetFileSystemEntries(srcDir))
            {
                string name = Path.GetFileName(entry);
                if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                // Don't move AuroraDesk's own folders if user accidentally put them on desktop
                string catFolder = classifier.ClassifyItem(entry, config);
                string destDir = Path.Combine(config.DesktopHubPath, catFolder);
                if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                string destPath = Path.Combine(destDir, name);
                if (File.Exists(destPath) || Directory.Exists(destPath))
                {
                    string stem = Path.GetFileNameWithoutExtension(name);
                    string ext = Path.GetExtension(name);
                    destPath = Path.Combine(destDir, $"{stem}_{DateTime.Now:MMdd_HHmmss}{ext}");
                }

                try
                {
                    if (Directory.Exists(entry)) Directory.Move(entry, destPath);
                    else File.Move(entry, destPath);

                    history.Add(new MoveHistoryItem
                    {
                        OriginalPath = entry,
                        MovedToPath = destPath,
                        Timestamp = DateTime.Now.ToString("o")
                    });
                    moved++;
                }
                catch { }
            }
        }

        if (moved > 0) SaveHistory(history);
        return moved;
    }

    public int RestoreAll()
    {
        var history = LoadHistory();
        int restored = 0;
        var remaining = new List<MoveHistoryItem>();

        foreach (var item in history)
        {
            try
            {
                bool exists = File.Exists(item.MovedToPath) || Directory.Exists(item.MovedToPath);
                if (exists && !File.Exists(item.OriginalPath) && !Directory.Exists(item.OriginalPath))
                {
                    string? parent = Path.GetDirectoryName(item.OriginalPath);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent)) Directory.CreateDirectory(parent);

                    if (Directory.Exists(item.MovedToPath)) Directory.Move(item.MovedToPath, item.OriginalPath);
                    else File.Move(item.MovedToPath, item.OriginalPath);
                    restored++;
                }
                else if (exists)
                {
                    remaining.Add(item);
                }
            }
            catch
            {
                remaining.Add(item);
            }
        }

        SaveHistory(remaining);
        return restored;
    }

    private List<MoveHistoryItem> LoadHistory()
    {
        if (!File.Exists(_historyPath)) return [];
        try { return JsonSerializer.Deserialize<List<MoveHistoryItem>>(File.ReadAllText(_historyPath)) ?? []; }
        catch { return []; }
    }

    private void SaveHistory(List<MoveHistoryItem> history)
    {
        File.WriteAllText(_historyPath, JsonSerializer.Serialize(history, JsonOpts));
    }
}
