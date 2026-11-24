using System;
using System.Collections.Generic;
using System.IO;
using AuroraDesk.Core.Models;

namespace AuroraDesk.Core.Services;

public sealed class DesktopCategoryIndex
{
    private readonly string _desktopPath;
    private readonly ClassifierService _classifier;
    private Dictionary<string, List<string>> _entries = new(StringComparer.OrdinalIgnoreCase);

    public DesktopCategoryIndex(string desktopPath, ClassifierService classifier)
    {
        _desktopPath = desktopPath;
        _classifier = classifier;
    }

    public int Count { get; private set; }

    public IReadOnlyList<string> GetEntries(string categoryPath) =>
        _entries.TryGetValue(Path.TrimEndingDirectorySeparator(Path.GetFullPath(categoryPath)), out var entries) ? entries : [];

    public void Refresh(AppConfig config)
    {
        _entries = new(StringComparer.OrdinalIgnoreCase);
        Count = 0;
        if (!config.KeepDesktopFilesInPlace || !Directory.Exists(_desktopPath)) return;

        try
        {
            string hubPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(config.DesktopHubPath));
            var categoryPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in config.Categories)
                categoryPaths[category.FolderName] = Path.TrimEndingDirectorySeparator(Path.Combine(hubPath, category.FolderName));

            foreach (string entry in Directory.EnumerateFileSystemEntries(_desktopPath, "*", new EnumerationOptions
                     { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System }))
            {
                if (Path.GetFileName(entry).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                    entry.Equals(hubPath, StringComparison.OrdinalIgnoreCase) ||
                    hubPath.StartsWith(entry + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !categoryPaths.TryGetValue(_classifier.ClassifyItem(entry, config), out var categoryPath)) continue;

                if (!_entries.TryGetValue(categoryPath, out var entries)) _entries[categoryPath] = entries = [];
                entries.Add(entry);
                Count++;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
