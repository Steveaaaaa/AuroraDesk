using System;
using System.IO;
using System.Linq;
using AuroraDesk.Core.Helpers;
using AuroraDesk.Core.Models;
using AuroraDesk.Core.Services;

int passed = 0;
int failed = 0;

void AssertTrue(bool cond, string name)
{
    if (cond) { passed++; Console.WriteLine($"  [PASS] {name}"); }
    else { failed++; Console.Error.WriteLine($"  [FAIL] {name}"); }
}

Console.WriteLine("=== AuroraDesk.Core Automated Tests ===");

// 1. PinyinHelper Tests
AssertTrue(PinyinHelper.GetInitials("微信") == "wx", "Pinyin initials: 微信 -> wx");
AssertTrue(PinyinHelper.GetInitials("燕云十六声") == "yysls", "Pinyin initials: 燕云十六声 -> yysls");
AssertTrue(PinyinHelper.GetInitials("毕业论文2026") == "bylw2026", "Pinyin initials alphanumeric: 毕业论文2026");
AssertTrue(PinyinHelper.MatchScore("wx", "微信") >= 80, "MatchScore: wx matches 微信");
AssertTrue(PinyinHelper.MatchScore("yyls", "燕云十六声") > 0, "MatchScore subsequence: yyls matches 燕云十六声");
AssertTrue(PinyinHelper.MatchScore("zzzz", "微信") == 0, "MatchScore non-match returns 0");

// 2. MathEvaluator Tests
AssertTrue(MathEvaluator.TryEvaluate("128*1.25", out double r1) && Math.Abs(r1 - 160) < 1e-6, "Math: 128*1.25 == 160");
AssertTrue(MathEvaluator.TryEvaluate("(10+20)/3", out double r2) && Math.Abs(r2 - 10) < 1e-6, "Math: (10+20)/3 == 10");
AssertTrue(MathEvaluator.TryEvaluate("2^10", out double r3) && Math.Abs(r3 - 1024) < 1e-6, "Math: 2^10 == 1024");
AssertTrue(!MathEvaluator.TryEvaluate("10/0", out _), "Math: divide by zero rejected");
AssertTrue(!MathEvaluator.TryEvaluate("hello", out _), "Math: plain text rejected");

// 3. ClassifierService Tests (Template 1 & Template 2 + Engineering software)
var classifier = new ClassifierService();
var cfg1 = new AppConfig { Categories = ConfigService.GetTemplateCategories("1") };
var cfg2 = new AppConfig { Categories = ConfigService.GetTemplateCategories("2") };

AssertTrue(classifier.ClassifyItem(@"C:\Fake\MATLAB R2024b.lnk", cfg1) == "02_工科仿真与编程", "T1 Engineering: MATLAB");
AssertTrue(classifier.ClassifyItem(@"C:\Fake\SolidWorks 2024.lnk", cfg1) == "02_工科仿真与编程", "T1 Engineering: SolidWorks");
AssertTrue(classifier.ClassifyItem(@"C:\Fake\Keil uVision5.lnk", cfg2) == "02_工科软件与AI", "T2 Engineering: Keil uVision5");
AssertTrue(classifier.ClassifyItem(@"C:\Fake\IntelliJ IDEA.lnk", cfg2) == "01_开发工具与IDE", "T2 Dev: IntelliJ IDEA");
AssertTrue(classifier.ClassifyItem(@"C:\Fake\微信.lnk", cfg2) == "03_社交影音与浏览", "T2 Social: 微信");
AssertTrue(classifier.ClassifyItem(@"C:\Fake\Stardew Valley.url", cfg2) == "04_游戏与娱乐休闲", "T2 Game: .url");
AssertTrue(classifier.ClassifyItem(@"C:\Fake\毕业论文终稿.pdf", cfg1) == "01_课程学习与论文", "T1 Study: 毕业论文.pdf");
AssertTrue(classifier.ClassifyItem(@"C:\Fake\神秘未知文件夹", cfg1) == "08_其他与待整理", "T1 Fallback: unknown -> 08_其他与待整理");

// 4. UsageStatsService Tests
string tmpDir = Path.Combine(Path.GetTempPath(), "AuroraDeskTest_" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(tmpDir);
try
{
    string f1 = Path.Combine(tmpDir, "AppA.txt");
    string f2 = Path.Combine(tmpDir, "AppB.txt");
    File.WriteAllText(f1, "a");
    File.WriteAllText(f2, "b");

    var stats = new UsageStatsService(tmpDir);
    stats.RecordLaunch(f1);
    stats.RecordLaunch(f1);
    System.Threading.Thread.Sleep(20);
    stats.RecordLaunch(f2);

    var recent = stats.GetRecent(8);
    var most = stats.GetMostUsed(8);
    AssertTrue(recent.Count == 2 && recent[0].Name == "AppB", "Recent order: AppB is latest");
    AssertTrue(most.Count == 2 && most[0].Name == "AppA" && most[0].Count == 2, "Most order: AppA has count 2");

    stats.TogglePin(f2);
    most = stats.GetMostUsed(8);
    AssertTrue(most[0].Name == "AppB", "Pinned item moves to top of MostUsed");

    stats.Flush();
    var reloaded = new UsageStatsService(tmpDir);
    AssertTrue(reloaded.GetMostUsed(8).Count == 2, "UsageStats persisted and reloaded cleanly");

    string openedFolder = Directory.CreateDirectory(Path.Combine(tmpDir, "OpenedFolder")).FullName;
    string unopenedFolder = Directory.CreateDirectory(Path.Combine(tmpDir, "UnopenedFolder")).FullName;
    string unopenedFile = Path.Combine(tmpDir, "A-Unopened.txt");
    File.WriteAllText(unopenedFile, "unopened");
    string[] drawerEntries = [unopenedFile, f1, unopenedFolder, f2, openedFolder];
    AssertTrue(reloaded.SortDrawerEntries(drawerEntries).SequenceEqual([f2, f1, openedFolder, unopenedFolder, unopenedFile]),
        "Drawer uses existing launch history and keeps original order for unused items");

    reloaded.RecordFolderOpen(openedFolder);
    AssertTrue(reloaded.SortDrawerEntries(drawerEntries)[0] == openedFolder && reloaded.GetRecent(8).Count == 2,
        "Opening a subfolder moves it to the top without adding it to the recent apps widget");
    reloaded.RecordLaunch(f2);
    reloaded.RecordLaunch(f1);
    AssertTrue(reloaded.SortDrawerEntries(drawerEntries).SequenceEqual([f1, f2, openedFolder, unopenedFolder, unopenedFile]),
        "The most recent click wins over pinning, item type, and previous usage");

    reloaded.Flush();
    AssertTrue(new UsageStatsService(tmpDir).SortDrawerEntries(drawerEntries).SequenceEqual([f1, f2, openedFolder, unopenedFolder, unopenedFile]),
        "Drawer retains its recent order after restart");

    string desktop = Directory.CreateDirectory(Path.Combine(tmpDir, "desktop")).FullName;
    string hub = Directory.CreateDirectory(Path.Combine(desktop, "DesktopHub")).FullName;
    string original = Path.Combine(desktop, "AuroraDeskKeepInPlace7857.pdf");
    File.WriteAllText(original, "desktop original");
    File.WriteAllText(Path.Combine(desktop, "desktop.ini"), "desktop metadata");
    var desktopConfig = new AppConfig
    {
        DesktopHubPath = hub,
        Categories = ConfigService.GetTemplateCategories("1"),
        KeepDesktopFilesInPlace = true,
        AutoTidyDesktop = true
    };
    string studyCategory = Path.Combine(hub, "01_课程学习与论文");
    var desktopIndex = new DesktopCategoryIndex(desktop, classifier);
    desktopIndex.Refresh(desktopConfig);
    AssertTrue(desktopIndex.Count == 1 && desktopIndex.GetEntries(studyCategory).SequenceEqual([original]),
        "In-place mode classifies desktop originals without listing DesktopHub or desktop.ini");
    var search = new SearchService();
    search.RebuildIndex(desktopConfig, desktopIndex);
    AssertTrue(search.Query("AuroraDeskKeepInPlace7857").Any(result => result.TargetPath == original),
        "In-place search opens the original desktop path");
    var backup = new BackupRestoreService(Path.Combine(tmpDir, "backup"));
    AssertTrue(backup.TidyDesktop(desktopConfig, classifier) == 0 && File.ReadAllText(original) == "desktop original" &&
        !Directory.Exists(studyCategory), "In-place mode bypasses moving even when auto-archive was enabled");
    desktopConfig.KeepDesktopFilesInPlace = false;
    desktopIndex.Refresh(desktopConfig);
    AssertTrue(desktopIndex.GetEntries(studyCategory).Count == 0, "Disabling in-place mode clears virtual entries without moving originals");

    string legacyDir = Path.Combine(tmpDir, "legacy");
    string configDir = Path.Combine(tmpDir, "settings");
    var legacyConfig = new ConfigService(legacyDir);
    legacyConfig.Current.Initialized = true;
    legacyConfig.Current.AutoStart = false;
    legacyConfig.Current.KeepDesktopFilesInPlace = true;
    legacyConfig.Current.HideDesktopIcons = false;
    legacyConfig.Current.EnableTaskbarTransparency = false;
    legacyConfig.Current.TaskbarMode = "Acrylic";
    legacyConfig.Current.TaskbarDarkOnMaximize = false;
    legacyConfig.Current.GlassOpacity = 0.71;
    legacyConfig.Current.LeftRecentPos = new() { Left = 73, Top = 140, HasCustomPosition = true };
    legacyConfig.Save();

    var migrated = new ConfigService(configDir, legacyDir);
    AssertTrue(migrated.Current.Initialized && !migrated.Current.EnableTaskbarTransparency &&
        migrated.Current.TaskbarMode == "Acrylic" && !migrated.Current.TaskbarDarkOnMaximize,
        "Config migrates active settings from the old installation");
    AssertTrue(!migrated.Current.AutoStart && !migrated.Current.HideDesktopIcons && migrated.Current.KeepDesktopFilesInPlace &&
        migrated.Current.LeftRecentPos.Left == 73 && migrated.Current.LeftRecentPos.HasCustomPosition,
        "Config restores system toggles and custom widget positions");

    migrated.Current.TaskbarMode = "Translucent";
    migrated.Save();
    var anotherLaunch = new ConfigService(configDir, Path.Combine(tmpDir, "other-copy"));
    AssertTrue(anotherLaunch.Current.TaskbarMode == "Translucent" && anotherLaunch.Current.GlassOpacity == 0.71,
        "Config survives save and restart from a different installation");
    File.WriteAllText(Path.Combine(configDir, "config.json"), "{incomplete");
    var recovered = new ConfigService(configDir);
    AssertTrue(recovered.Current.TaskbarMode == "Acrylic" && recovered.Current.Initialized,
        "Config recovers previous settings when the main file is damaged");
    AssertTrue(new ConfigService(configDir).Current.TaskbarMode == "Acrylic" &&
        Directory.GetFiles(configDir, "*.tmp").Length == 0,
        "Config repairs the main file without leaving partial writes");
}
finally
{
    string resolvedTemp = Path.GetFullPath(tmpDir);
    string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
    if (Path.GetDirectoryName(resolvedTemp)?.Equals(tempRoot, StringComparison.OrdinalIgnoreCase) == true)
        Directory.Delete(resolvedTemp, true);
}

Console.WriteLine($"\nSummary: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
