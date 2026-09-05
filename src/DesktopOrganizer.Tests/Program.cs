using System.Text.Json;
using DesktopOrganizer.Core.Models;
using DesktopOrganizer.Core.Services;
using DesktopOrganizer.Services;

namespace DesktopOrganizer.Tests;

internal static class Program
{
    private static readonly (string Name, Action Test)[] Tests =
    [
        ("Normalize repairs layout invariants", NormalizeRepairsLayoutInvariants),
        ("Empty layout remains valid", EmptyLayoutRemainsValid),
        ("Manual move wins over rules", ManualMoveWinsOverRules),
        ("Extension and name rules apply", ExtensionAndNameRulesApply),
        ("Delete zone cleans dependent state", DeleteZoneCleansDependentState),
        ("Deleting inbox promotes another zone", DeletingInboxPromotesAnotherZone),
        ("Smart folder zone stays independent from inbox", SmartFolderZoneStaysIndependentFromInbox),
        ("Smart folder catalog filters recursively", SmartFolderCatalogFiltersRecursively),
        ("File moves keep both names and undo safely", FileMovesKeepBothNamesAndUndoSafely),
        ("Rename keeps placement", RenameKeepsPlacement),
        ("History stores deep snapshots", HistoryStoresDeepSnapshots),
        ("Pinned state survives normalization and cloning", PinnedStateSurvivesNormalizationAndCloning),
        ("Display mode survives cloning and persistence", DisplayModeSurvivesCloningAndPersistence),
        ("Collapsed zones use capsule bounds while dragging", CollapsedZonesUseCapsuleBoundsWhileDragging),
        ("Layout store migrates schema 3", LayoutStoreMigratesSchemaThree),
        ("Layout store recovers backup", LayoutStoreRecoversBackup),
        ("Settings default to non-invasive desktop mode", SettingsDefaultToNonInvasiveDesktopMode),
        ("Second launch signals the existing instance", SecondLaunchSignalsExistingInstance),
        ("Settings store round-trips", SettingsStoreRoundTrips),
        ("Backup bundle round-trips", BackupBundleRoundTrips),
        ("Desktop monitor reports changes and renames", DesktopMonitorReportsChangesAndRenames),
    ];

    public static int Main()
    {
        var failures = new List<string>();
        foreach (var (name, test) in Tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures.Add(name);
                Console.Error.WriteLine($"FAIL  {name}\n      {exception.Message}");
            }
        }

        Console.WriteLine($"\n{Tests.Length - failures.Count}/{Tests.Length} checks passed.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void SettingsDefaultToNonInvasiveDesktopMode()
    {
        var settings = new AppSettings();
        Assert(!settings.HideNativeDesktopIcons, "Native desktop icons must remain visible until the user opts in.");
        Assert(!settings.LaunchAtStartup, "Startup registration must remain opt-in.");
    }

    private static void PinnedStateSurvivesNormalizationAndCloning()
    {
        var snapshot = new LayoutSnapshot
        {
            Zones =
            [
                new ZoneLayout { Name = "收件箱", IsInbox = true },
                new ZoneLayout { Name = "固定分区", IsPinned = true },
            ],
        };

        LayoutStateManager.Normalize(snapshot);
        var clone = snapshot.Clone();

        Assert(snapshot.SchemaVersion == LayoutSnapshot.CurrentSchemaVersion, "Pinned layout was not migrated.");
        Assert(clone.Zones.Single(zone => zone.Name == "固定分区").IsPinned, "Pinned state was lost while cloning.");
    }

    private static void CollapsedZonesUseCapsuleBoundsWhileDragging()
    {
        var layout = new ZoneLayout
        {
            X = 2_000,
            Y = 2_000,
            Width = 560,
            Height = 390,
            IsCollapsed = true,
        };

        layout.ClampTo(1_280, 720);

        Assert(layout.X == 1_280 - ZoneLayout.CollapsedWidth, "Collapsed zone still used expanded width at the right edge.");
        Assert(layout.Y == 720 - ZoneLayout.CollapsedHeight, "Collapsed zone still used expanded height at the bottom edge.");
        Assert(layout.Width == 560 && layout.Height == 390, "Collapsed dragging changed the stored expanded size.");

        layout.IsCollapsed = false;
        layout.ClampTo(1_280, 720);
        Assert(layout.X == 720 && layout.Y == 330, "Expanded zone was not moved back inside the workspace.");
    }

    private static void DisplayModeSurvivesCloningAndPersistence()
    {
        var snapshot = new LayoutSnapshot
        {
            Zones =
            [
                new ZoneLayout
                {
                    Name = "图标智能分区",
                    Kind = ZoneKind.SmartFolder,
                    SourcePath = "C:\\Projects",
                    DisplayMode = ZoneDisplayMode.Grid,
                },
            ],
        };

        Assert(snapshot.Clone().Zones[0].DisplayMode == ZoneDisplayMode.Grid, "Display mode was lost while cloning.");

        using var directory = new TemporaryDirectory();
        var store = new LayoutStore(directory.Path);
        Assert(store.Save(snapshot), "Layout with a display mode should be saved.");
        var restored = store.Load();
        Assert(restored?.Zones[0].DisplayMode == ZoneDisplayMode.Grid, "Display mode was lost after reload.");
    }

    private static void SecondLaunchSignalsExistingInstance()
    {
        using var activated = new ManualResetEventSlim();
        using var service = new SingleInstanceActivationService(activated.Set);

        Assert(SingleInstanceActivationService.SignalExistingInstance(), "The activation signal was not sent.");
        Assert(activated.Wait(TimeSpan.FromSeconds(2)), "The running instance did not receive activation.");
    }

    private static void NormalizeRepairsLayoutInvariants()
    {
        var duplicateId = "duplicate";
        var snapshot = new LayoutSnapshot
        {
            SchemaVersion = 3,
            Zones =
            [
                new ZoneLayout { Id = "inbox", Name = "收件箱", IsInbox = true },
                new ZoneLayout { Id = duplicateId, Name = "  工作  " },
                new ZoneLayout { Id = duplicateId, Name = "" },
            ],
            Placements =
            [
                new DesktopItemPlacement { Path = "a.txt", ZoneId = duplicateId },
                new DesktopItemPlacement { Path = "a.txt", ZoneId = duplicateId },
                new DesktopItemPlacement { Path = "missing.txt", ZoneId = "missing" },
            ],
        };

        LayoutStateManager.Normalize(snapshot);

        Assert(snapshot.SchemaVersion == LayoutSnapshot.CurrentSchemaVersion, "Schema was not upgraded.");
        Assert(snapshot.Zones.Count(zone => zone.IsInbox) == 1, "Exactly one inbox is required.");
        Assert(snapshot.Zones.Select(zone => zone.Id).Distinct().Count() == snapshot.Zones.Count, "Zone IDs must be unique.");
        Assert(snapshot.Zones.Any(zone => zone.Name == "工作"), "Zone names should be trimmed.");
        Assert(snapshot.Placements.Count == 1, "Placements should be valid and deduplicated.");
    }

    private static void EmptyLayoutRemainsValid()
    {
        var snapshot = new LayoutSnapshot
        {
            Placements = [new DesktopItemPlacement { Path = "orphan.txt", ZoneId = "missing" }],
            Rules = [new OrganizationRule { Name = "orphan", Pattern = ".txt", TargetZoneId = "missing" }],
        };

        LayoutStateManager.Normalize(snapshot);

        Assert(snapshot.Zones.Count == 0, "Normalization should not recreate a deleted final zone.");
        Assert(snapshot.Placements.Count == 0, "Placements without zones should be removed.");
        Assert(snapshot.Rules.All(rule => !rule.IsEnabled), "Rules without target zones should be disabled.");

        using var directory = new TemporaryDirectory();
        var store = new LayoutStore(directory.Path);
        Assert(store.Save(snapshot), "Empty layout should be saved.");
        var restored = store.Load();
        Assert(restored is not null && restored.Zones.Count == 0, "Empty layout should remain empty after reload.");
    }

    private static void ManualMoveWinsOverRules()
    {
        var snapshot = CreateRuleSnapshot(out var inbox, out var documents, out var pictures);
        var item = new DesktopItemDescriptor("report.pdf", "C:\\Desktop\\report.pdf");

        Assert(LayoutStateManager.MoveItem(snapshot, item.Path, pictures.Id), "Manual move should change state.");
        LayoutStateManager.ApplyRules(snapshot, [item]);

        var placement = snapshot.Placements.Single();
        Assert(placement.ZoneId == pictures.Id, "Rule overrode a manual placement.");
        Assert(placement.Kind == PlacementKind.Manual, "Manual placement kind was lost.");
        Assert(inbox.IsInbox && documents.Id != pictures.Id, "Test layout is invalid.");
    }

    private static void ExtensionAndNameRulesApply()
    {
        var snapshot = CreateRuleSnapshot(out _, out var documents, out var pictures);
        snapshot.Rules.Add(new OrganizationRule
        {
            Name = "截图",
            Priority = 1,
            MatchKind = RuleMatchKind.NameContains,
            Pattern = "截图",
            TargetZoneId = pictures.Id,
        });
        var items = new[]
        {
            new DesktopItemDescriptor("report.pdf", "C:\\Desktop\\report.pdf"),
            new DesktopItemDescriptor("项目截图.png", "C:\\Desktop\\项目截图.png"),
            new DesktopItemDescriptor("notes.txt", "C:\\Desktop\\notes.txt"),
        };

        Assert(LayoutStateManager.ApplyRules(snapshot, items), "Rules should produce placements.");
        Assert(snapshot.Placements.Any(item => item.Path.EndsWith("report.pdf") && item.ZoneId == documents.Id), "Extension rule failed.");
        Assert(snapshot.Placements.Any(item => item.Path.EndsWith("项目截图.png") && item.ZoneId == pictures.Id), "Name rule failed.");
        Assert(snapshot.Placements.All(item => item.Kind == PlacementKind.Rule), "Rule placements need a source marker.");
    }

    private static void DeleteZoneCleansDependentState()
    {
        var snapshot = CreateRuleSnapshot(out _, out var documents, out _);
        snapshot.Placements.Add(new DesktopItemPlacement { Path = "a.pdf", ZoneId = documents.Id });

        Assert(LayoutStateManager.DeleteZone(snapshot, documents.Id), "Zone should be deleted.");
        Assert(snapshot.Placements.All(item => item.ZoneId != documents.Id), "Dependent placement survived deletion.");
        Assert(snapshot.Rules.All(rule => rule.TargetZoneId != documents.Id), "Dependent rule survived deletion.");
    }

    private static void DeletingInboxPromotesAnotherZone()
    {
        var snapshot = CreateRuleSnapshot(out var inbox, out var documents, out _);
        snapshot.Placements.Add(new DesktopItemPlacement { Path = "manual.pdf", ZoneId = documents.Id });

        Assert(LayoutStateManager.DeleteZone(snapshot, inbox.Id), "Inbox should be deletable when another zone exists.");
        Assert(snapshot.Zones.Count(zone => zone.IsInbox) == 1, "A replacement inbox was not selected.");
        Assert(documents.IsInbox, "The first remaining zone should become the replacement inbox.");
        Assert(snapshot.Placements.All(placement => placement.ZoneId != documents.Id), "Replacement inbox kept stale explicit placements.");
        Assert(snapshot.Rules.Where(rule => rule.TargetZoneId == documents.Id).All(rule => !rule.IsEnabled), "Rules targeting the replacement inbox should be disabled.");

        var onlyZone = new LayoutSnapshot
        {
            Zones = [new ZoneLayout { Name = "唯一分区", IsInbox = true }],
        };
        Assert(LayoutStateManager.DeleteZone(onlyZone, onlyZone.Zones[0].Id), "The final zone should be deletable.");
        Assert(onlyZone.Zones.Count == 0, "Deleting the final zone should leave an empty layout.");
    }

    private static void SmartFolderZoneStaysIndependentFromInbox()
    {
        var smartZone = new ZoneLayout
        {
            Name = "图纸",
            Kind = ZoneKind.SmartFolder,
            SourcePath = " C:\\Projects ",
            Extensions = ["DWG", ".dwg", " dxf "],
        };
        var snapshot = new LayoutSnapshot { Zones = [smartZone] };

        LayoutStateManager.Normalize(snapshot);

        Assert(snapshot.Zones.Count == 1, "Normalization added an unexpected desktop inbox.");
        Assert(!smartZone.IsInbox, "A smart folder zone became a desktop inbox.");
        Assert(smartZone.Extensions.SequenceEqual([".dwg", ".dxf"]), "Extensions were not normalized and deduplicated.");
    }

    private static void SmartFolderCatalogFiltersRecursively()
    {
        using var directory = new TemporaryDirectory();
        var nested = Path.Combine(directory.Path, "nested");
        var deeper = Path.Combine(nested, "deeper");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(deeper);
        File.WriteAllText(Path.Combine(directory.Path, "drawing.dwg"), "one");
        File.WriteAllText(Path.Combine(directory.Path, "notes.pdf"), "two");
        File.WriteAllText(Path.Combine(nested, "detail.DWG"), "three");
        var zone = new ZoneLayout
        {
            Kind = ZoneKind.SmartFolder,
            SourcePath = directory.Path,
            Extensions = [".dwg"],
        };
        var catalog = new SmartFolderCatalog();

        var topLevel = catalog.ScanAsync(zone).GetAwaiter().GetResult();
        Assert(topLevel.Error is null && topLevel.Entries.Count(entry => !entry.IsDirectory) == 1, "Top-level format filtering failed.");
        Assert(topLevel.Entries.Any(entry => entry.IsDirectory && entry.Name == "nested"), "Child folders were not exposed for navigation.");

        zone.IncludeSubfolders = true;
        var recursive = catalog.ScanAsync(zone).GetAwaiter().GetResult();
        Assert(recursive.Error is null && recursive.Entries.Count(entry => !entry.IsDirectory) == 2, "Recursive format filtering failed.");
        Assert(recursive.Entries.Any(entry => entry.RelativeDirectory == "nested"), "Relative directory metadata is missing.");

        zone.IncludeSubfolders = false;
        var nestedResult = catalog.ScanAsync(zone, nested).GetAwaiter().GetResult();
        Assert(nestedResult.Entries.Any(entry => entry.IsDirectory && entry.Name == "deeper"), "Nested browsing did not expose the next folder level.");
        Assert(nestedResult.Entries.Any(entry => entry.Name == "detail.DWG"), "Nested browsing did not show files from the current folder.");
        Assert(nestedResult.Entries.All(entry => entry.Name != "drawing.dwg"), "Nested browsing leaked files from the root folder.");
        Assert(SmartFolderCatalog.IsPathWithinRoot(directory.Path, deeper), "A valid descendant was rejected.");
        Assert(!SmartFolderCatalog.IsPathWithinRoot(nested, directory.Path), "Navigation escaped above the configured root.");
    }

    private static void FileMovesKeepBothNamesAndUndoSafely()
    {
        using var directory = new TemporaryDirectory();
        var sourceDirectory = Path.Combine(directory.Path, "source");
        var targetDirectory = Path.Combine(directory.Path, "target");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(targetDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "drawing.dwg");
        var existingTarget = Path.Combine(targetDirectory, "drawing.dwg");
        File.WriteAllText(sourcePath, "new");
        File.WriteAllText(existingTarget, "existing");
        var service = new FileOperationService();

        var move = service.MoveFiles([sourcePath], targetDirectory);
        Assert(move.Failures.Count == 0 && move.Moves.Count == 1, "File move failed.");
        Assert(Path.GetFileName(move.Moves[0].DestinationPath) == "drawing (2).dwg", "Name conflict did not keep both files.");
        Assert(File.ReadAllText(existingTarget) == "existing", "Existing target was overwritten.");

        var undo = service.UndoMoves(move.Moves);
        Assert(undo.Failures.Count == 0 && File.Exists(sourcePath), "File move could not be undone.");
    }

    private static void RenameKeepsPlacement()
    {
        var snapshot = CreateRuleSnapshot(out _, out var documents, out _);
        snapshot.Placements.Add(new DesktopItemPlacement { Path = "old.pdf", ZoneId = documents.Id });

        Assert(LayoutStateManager.RenameItemPath(snapshot, "old.pdf", "new.pdf"), "Rename should update placement.");
        Assert(snapshot.Placements.Single().Path == "new.pdf", "Placement path was not updated.");
    }

    private static void HistoryStoresDeepSnapshots()
    {
        var snapshot = CreateRuleSnapshot(out _, out _, out _);
        var history = new LayoutHistory(2);
        history.Push(snapshot);
        snapshot.Zones[0].Name = "Changed";
        snapshot.Rules[0].Pattern = ".docx";

        var restored = history.Undo() ?? throw new InvalidOperationException("Undo snapshot is missing.");
        Assert(restored.Zones[0].Name != "Changed", "Zone clone was shallow.");
        Assert(restored.Rules[0].Pattern == ".pdf", "Rule clone was shallow.");
    }

    private static void LayoutStoreMigratesSchemaThree()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(directory.Path, "layout.json"),
            """
            {"SchemaVersion":3,"Zones":[{"Id":"inbox","Name":"收件箱","IsInbox":true}],"Placements":[]}
            """);

        var snapshot = new LayoutStore(directory.Path).Load() ??
            throw new InvalidOperationException("Schema 3 layout did not load.");
        Assert(snapshot.SchemaVersion == LayoutSnapshot.CurrentSchemaVersion, "Schema 3 was not migrated.");
        Assert(snapshot.Rules.Count == 0, "Migration should initialize rules.");
    }

    private static void LayoutStoreRecoversBackup()
    {
        using var directory = new TemporaryDirectory();
        var store = new LayoutStore(directory.Path);
        var snapshot = CreateRuleSnapshot(out _, out _, out _);
        snapshot.Zones[0].Name = "Version A";
        Assert(store.Save(snapshot), "Initial layout save failed.");
        snapshot.Zones[0].Name = "Version B";
        Assert(store.Save(snapshot), "Second layout save failed.");
        File.WriteAllText(Path.Combine(directory.Path, "layout.json"), "not json");

        var recovered = store.Load();
        Assert(recovered?.Zones[0].Name == "Version A", "Backup layout was not recovered.");
        Assert(Directory.EnumerateFiles(directory.Path, "layout.corrupt-*.json").Any(), "Invalid primary was not quarantined.");
    }

    private static void SettingsStoreRoundTrips()
    {
        using var directory = new TemporaryDirectory();
        var store = new SettingsStore(directory.Path);
        var settings = new AppSettings
        {
            AutoApplyRules = false,
            ShowCommandToolbar = false,
            LaunchAtStartup = true,
            HideNativeDesktopIcons = false,
            HasConfirmedRealFileMoves = true,
        };

        Assert(store.Save(settings), "Settings save failed.");
        var loaded = store.Load();
        Assert(
            !loaded.AutoApplyRules &&
            !loaded.ShowCommandToolbar &&
            loaded.LaunchAtStartup &&
            !loaded.HideNativeDesktopIcons &&
            loaded.HasConfirmedRealFileMoves,
            "Settings did not round-trip.");
    }

    private static void DesktopMonitorReportsChangesAndRenames()
    {
        using var directory = new TemporaryDirectory();
        using var monitor = new DesktopChangeMonitor();
        using var changed = new ManualResetEventSlim();
        using var renamed = new ManualResetEventSlim();
        DesktopPathRenamedEventArgs? renameArgs = null;
        monitor.Changed += (_, _) => changed.Set();
        monitor.Renamed += (_, args) =>
        {
            renameArgs = args;
            renamed.Set();
        };
        monitor.Start([directory.Path]);

        var oldPath = Path.Combine(directory.Path, "old.txt");
        var newPath = Path.Combine(directory.Path, "new.txt");
        File.WriteAllText(oldPath, "test");
        Assert(changed.Wait(TimeSpan.FromSeconds(5)), "File creation was not reported.");
        File.Move(oldPath, newPath);
        Assert(renamed.Wait(TimeSpan.FromSeconds(5)), "Rename was not reported.");
        Assert(renameArgs?.OldPath == oldPath && renameArgs.NewPath == newPath, "Rename paths are incorrect.");
    }

    private static void BackupBundleRoundTrips()
    {
        using var directory = new TemporaryDirectory();
        var service = new BackupService(directory.Path);
        var snapshot = CreateRuleSnapshot(out _, out _, out _);
        var settings = new AppSettings { AutoApplyRules = false, HideNativeDesktopIcons = false };
        var path = Path.Combine(directory.Path, "backup.desktoporganizer");

        Assert(service.Export(path, snapshot, settings), "Backup export failed.");
        var imported = service.Import(path) ?? throw new InvalidOperationException("Backup import failed.");
        Assert(imported.Layout.Zones.Count == snapshot.Zones.Count, "Backup lost zones.");
        Assert(imported.Layout.Rules.Count == snapshot.Rules.Count, "Backup lost rules.");
        Assert(!imported.Settings.AutoApplyRules && !imported.Settings.HideNativeDesktopIcons, "Backup lost settings.");
    }

    private static LayoutSnapshot CreateRuleSnapshot(
        out ZoneLayout inbox,
        out ZoneLayout documents,
        out ZoneLayout pictures)
    {
        inbox = new ZoneLayout { Name = "收件箱", IsInbox = true };
        documents = new ZoneLayout { Name = "文档" };
        pictures = new ZoneLayout { Name = "图片" };
        return new LayoutSnapshot
        {
            Zones = [inbox, documents, pictures],
            Rules =
            [
                new OrganizationRule
                {
                    Name = "PDF 文档",
                    MatchKind = RuleMatchKind.Extension,
                    Pattern = ".pdf",
                    TargetZoneId = documents.Id,
                },
            ],
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"DesktopOrganizer.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path) &&
                System.IO.Path.GetFileName(Path).StartsWith("DesktopOrganizer.Tests-", StringComparison.Ordinal))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
