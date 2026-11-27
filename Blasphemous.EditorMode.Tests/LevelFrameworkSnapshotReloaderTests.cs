using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class LevelFrameworkSnapshotReloaderTests
{
    [Fact]
    public void Successful_reload_replaces_the_next_preload_snapshot()
    {
        FrameworkObjectData oldAddition = Addition("old");
        FrameworkObjectData expectedAddition = Addition("next");
        FakeSnapshotStore store = new(
            Snapshot(oldAddition),
            Snapshot(expectedAddition));
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            Document(expectedAddition));

        Assert.Equal(LevelFrameworkReloadStatus.Succeeded, result.Status);
        Assert.True(result.Succeeded);
        Assert.False(result.RestartRequired);
        Assert.Equal(1, store.CaptureCalls);
        Assert.Equal(1, store.ReloadCalls);
        Assert.Equal(0, store.RestoreCalls);
        Assert.Same(store.ReloadSnapshot, store.Current);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkReloadSucceeded
                && entry.Severity == ApplySeverity.Info);
    }

    [Fact]
    public void Preserved_addition_reload_matches_its_original_known_fields()
    {
        FrameworkObjectData preserved = FrameworkObjectData.PreservedAddition(
            JObject.Parse(
                "{\"type\":\"other-mod-type\",\"id\":\"preserved-1\","
                + "\"position\":{\"x\":7,\"y\":8,\"z\":9},"
                + "\"properties\":[\"key=value\"],\"custom\":true}"));
        FrameworkObjectData actual = new(
            null,
            null,
            "other-mod-type",
            "preserved-1",
            new FrameworkVector(7f, 8f, 9f),
            FrameworkVector.Zero,
            FrameworkVector.One,
            string.Empty,
            ["key=value"]);
        FakeSnapshotStore store = new(
            Snapshot(),
            Snapshot([actual]));
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            new FrameworkLevelEdit([preserved], [], []));

        Assert.Equal(LevelFrameworkReloadStatus.Succeeded, result.Status);
        Assert.Equal(0, store.RestoreCalls);
    }

    [Fact]
    public void Preserved_addition_without_id_matches_by_its_original_fields()
    {
        FrameworkObjectData preserved = FrameworkObjectData.PreservedAddition(
            JObject.Parse(
                "{\"type\":\"other-mod-type\","
                + "\"position\":{\"x\":7,\"y\":8,\"z\":9},"
                + "\"properties\":[\"key=value\"]}"));
        FrameworkObjectData actual = new(
            null,
            null,
            "other-mod-type",
            null,
            new FrameworkVector(7f, 8f, 9f),
            FrameworkVector.Zero,
            FrameworkVector.One,
            string.Empty,
            ["key=value"]);
        FakeSnapshotStore store = new(
            Snapshot(),
            Snapshot([actual]));
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            new FrameworkLevelEdit([preserved], [], []));

        Assert.Equal(LevelFrameworkReloadStatus.Succeeded, result.Status);
        Assert.Equal(0, store.RestoreCalls);
    }

    [Fact]
    public void Preserved_addition_with_wrong_content_fails_and_restores()
    {
        FrameworkObjectData preserved = FrameworkObjectData.PreservedAddition(
            JObject.Parse(
                "{\"type\":\"other-mod-type\",\"id\":\"preserved-1\","
                + "\"position\":{\"x\":7,\"y\":8,\"z\":9},"
                + "\"properties\":[\"key=value\"]}"));
        FrameworkObjectData actual = new(
            null,
            null,
            "other-mod-type",
            "preserved-1",
            new FrameworkVector(0f, 0f, 0f),
            FrameworkVector.Zero,
            FrameworkVector.One,
            string.Empty,
            ["key=value"]);
        LevelFrameworkSnapshot before = Snapshot();
        FakeSnapshotStore store = new(
            before,
            Snapshot([actual]));
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            new FrameworkLevelEdit([preserved], [], []));

        Assert.Equal(LevelFrameworkReloadStatus.FailedRolledBack, result.Status);
        Assert.Equal(1, store.RestoreCalls);
        Assert.Same(before, store.Current);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkReloadValidationFailed
                && entry.Location.StartsWith("additions[id='preserved-1']", StringComparison.Ordinal));
    }

    [Fact]
    public void Preserved_addition_without_comparable_fields_fails_and_restores()
    {
        FrameworkObjectData preserved = FrameworkObjectData.PreservedAddition(
            JObject.Parse("{\"custom\":true}"));
        LevelFrameworkSnapshot before = Snapshot();
        FakeSnapshotStore store = new(
            before,
            Snapshot([Addition("some-other-entry")]));
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            new FrameworkLevelEdit([preserved], [], []));

        Assert.Equal(LevelFrameworkReloadStatus.FailedRolledBack, result.Status);
        Assert.Equal(1, store.RestoreCalls);
        Assert.Same(before, store.Current);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkReloadValidationFailed
                && entry.Location.StartsWith("additions[id='']", StringComparison.Ordinal));
    }

    [Fact]
    public void Successful_reload_validates_additions_modifications_and_deletions()
    {
        FrameworkObjectData addition = Addition("next");
        FrameworkObjectData modification = Modification("MIDDLEGROUND/AfterPlayer/Props/existing");
        FrameworkObjectData deletion = Deletion("MIDDLEGROUND/AfterPlayer/Props/removed");
        FakeSnapshotStore store = new(
            Snapshot(),
            Snapshot([addition], [modification], [deletion]));
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            new FrameworkLevelEdit([addition], [modification], [deletion]));

        Assert.Equal(LevelFrameworkReloadStatus.Succeeded, result.Status);
        Assert.Equal(0, store.RestoreCalls);
        Assert.Same(store.ReloadSnapshot, store.Current);
    }

    [Fact]
    public void Reload_failure_restores_the_previous_snapshot_once()
    {
        LevelFrameworkSnapshot before = Snapshot(Addition("old"));
        FakeSnapshotStore store = new(before, Snapshot(Addition("next")))
        {
            ReloadFails = true
        };
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            Document(Addition("next")));

        Assert.Equal(LevelFrameworkReloadStatus.FailedRolledBack, result.Status);
        Assert.False(result.Succeeded);
        Assert.False(result.RestartRequired);
        Assert.Equal(1, store.CaptureCalls);
        Assert.Equal(1, store.ReloadCalls);
        Assert.Equal(1, store.RestoreCalls);
        Assert.Same(before, store.Current);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkReloadFailed
                && entry.Severity == ApplySeverity.Error);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkReloadRolledBack
                && entry.Severity == ApplySeverity.Warning);
    }

    [Fact]
    public void Capture_failure_does_not_reload_or_restore()
    {
        FakeSnapshotStore store = new(
            Snapshot(Addition("old")),
            Snapshot(Addition("next")))
        {
            CaptureFails = true
        };
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            Document(Addition("next")));

        Assert.Equal(LevelFrameworkReloadStatus.FailedBeforeMutation, result.Status);
        Assert.Equal(1, store.CaptureCalls);
        Assert.Equal(0, store.ReloadCalls);
        Assert.Equal(0, store.RestoreCalls);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkReloadCaptureFailed
                && entry.Severity == ApplySeverity.Error);
    }

    [Fact]
    public void Modification_and_deletion_mismatch_report_each_operation_and_restore()
    {
        FrameworkObjectData expectedModification = Modification(
            "MIDDLEGROUND/AfterPlayer/Props/expected-modification");
        FrameworkObjectData expectedDeletion = Deletion(
            "MIDDLEGROUND/AfterPlayer/Props/expected-deletion");
        LevelFrameworkSnapshot before = Snapshot(
            [],
            [expectedModification],
            [expectedDeletion]);
        FakeSnapshotStore store = new(
            before,
            Snapshot(
                [],
                [Modification("MIDDLEGROUND/AfterPlayer/Props/wrong-modification")],
                [Deletion("MIDDLEGROUND/AfterPlayer/Props/wrong-deletion")]));
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            new FrameworkLevelEdit([], [expectedModification], [expectedDeletion]));

        Assert.Equal(LevelFrameworkReloadStatus.FailedRolledBack, result.Status);
        Assert.Equal(1, store.RestoreCalls);
        Assert.Same(before, store.Current);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkReloadValidationFailed
                && entry.Location.StartsWith("modifications[", StringComparison.Ordinal));
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkReloadValidationFailed
                && entry.Location.StartsWith("deletions[", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_failure_restores_the_previous_snapshot()
    {
        LevelFrameworkSnapshot before = Snapshot(Addition("old"));
        FakeSnapshotStore store = new(
            before,
            Snapshot(Addition("wrong")));
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            Document(Addition("expected")));

        Assert.Equal(LevelFrameworkReloadStatus.FailedRolledBack, result.Status);
        Assert.Equal(1, store.ReloadCalls);
        Assert.Equal(1, store.RestoreCalls);
        Assert.Same(before, store.Current);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkReloadValidationFailed
                && entry.Location.Contains("id='expected'"));
    }

    [Fact]
    public void Restore_failure_requires_restart_without_claiming_preload_state()
    {
        FakeSnapshotStore store = new(
            Snapshot(Addition("old")),
            Snapshot(Addition("next")))
        {
            ReloadFails = true,
            RestoreFails = true
        };
        LevelFrameworkSnapshotReloader reloader = new(store);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            Document(Addition("next")));

        Assert.Equal(LevelFrameworkReloadStatus.FailedRestartRequired, result.Status);
        Assert.True(result.RestartRequired);
        Assert.Equal(1, store.ReloadCalls);
        Assert.Equal(1, store.RestoreCalls);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Code == ReportCodes.FrameworkRestartRequired
                && entry.Severity == ApplySeverity.Error
                && entry.Message.IndexOf("restart", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain(
            result.Report.Entries,
            entry => entry.Message.IndexOf(
                "next preload is ready",
                StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Unsupported_signatures_fail_before_capture_or_mutation()
    {
        FakeSnapshotStore store = new(
            Snapshot(Addition("old")),
            Snapshot(Addition("next")));
        ApplyReport compatibility = new();
        compatibility.Error(
            ReportCodes.FrameworkReloadUnsupported,
            "framework.version",
            "Expected 0.2.0.0 but found 9.9.9.9.",
            skipped: false);
        compatibility.Error(
            ReportCodes.FrameworkReloadUnsupported,
            "framework.field._additions",
            "Snapshot field signature did not match.",
            skipped: false);
        LevelFrameworkSnapshotStoreBinding binding = new(store, compatibility);
        LevelFrameworkSnapshotReloader reloader = new(binding);

        LevelFrameworkReloadResult result = reloader.Reload(
            RoomKey,
            Document(Addition("next")));

        Assert.Equal(LevelFrameworkReloadStatus.Unsupported, result.Status);
        Assert.Equal(0, store.CaptureCalls);
        Assert.Equal(0, store.ReloadCalls);
        Assert.Equal(0, store.RestoreCalls);
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Location == "framework.version");
        Assert.Contains(
            result.Report.Entries,
            entry => entry.Location == "framework.field._additions");
    }

    [Fact]
    public void Failed_reload_is_not_retried_automatically()
    {
        FakeSnapshotStore store = new(
            Snapshot(Addition("old")),
            Snapshot(Addition("next")))
        {
            ReloadFails = true
        };
        LevelFrameworkSnapshotReloader reloader = new(store);

        reloader.Reload(RoomKey, Document(Addition("next")));

        Assert.Equal(1, store.ReloadCalls);
        Assert.Equal(1, store.RestoreCalls);
    }

    [Fact]
    public void Successful_export_runs_reload_only_after_the_canonical_file_exists()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        bool fileExistedDuringReload = false;
        FakeSnapshotStore store = new(
            Snapshot(),
            Snapshot())
        {
            OnReload = () => fileExistedDuringReload = File.Exists(path)
        };
        LevelExportWorkflow workflow = new(
            CreateSession(),
            new LevelFrameworkSnapshotReloader(store));
        try
        {
            LevelEditSessionExportResult result = workflow.Export(path);

            Assert.True(result.Succeeded, result.StatusMessage);
            Assert.NotNull(result.FrameworkReload);
            Assert.Equal(LevelFrameworkReloadStatus.Succeeded, result.FrameworkReload.Status);
            Assert.True(fileExistedDuringReload);
            Assert.True(File.Exists(path));
            Assert.Equal(1, store.ReloadCalls);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_canonical_export_does_not_run_reload()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "WRONG.json");
        FakeSnapshotStore store = new(
            Snapshot(),
            Snapshot());
        LevelExportWorkflow workflow = new(
            CreateSession(),
            new LevelFrameworkSnapshotReloader(store));
        try
        {
            LevelEditSessionExportResult result = workflow.Export(path);

            Assert.False(result.Succeeded);
            Assert.Null(result.FrameworkReload);
            Assert.Equal(0, store.CaptureCalls);
            Assert.Equal(0, store.ReloadCalls);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Reload_failure_keeps_the_canonical_export_committed()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        FakeSnapshotStore store = new(
            Snapshot(),
            Snapshot())
        {
            ReloadFails = true
        };
        LevelExportWorkflow workflow = new(
            CreateSession(),
            new LevelFrameworkSnapshotReloader(store));
        try
        {
            LevelEditSessionExportResult result = workflow.Export(path);

            Assert.True(result.Succeeded, result.StatusMessage);
            Assert.Equal(
                LevelFrameworkReloadStatus.FailedRolledBack,
                result.FrameworkReload.Status);
            Assert.True(File.Exists(path));
            Assert.True(LevelFrameworkAdapter.Parse(File.ReadAllText(path)).IsValid);
            Assert.False(result.Report.HasErrors);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private const string RoomKey = "D03Z02S06";

    private static FrameworkLevelEdit Document(params FrameworkObjectData[] additions)
    {
        return new FrameworkLevelEdit(additions, [], []);
    }

    private static LevelFrameworkSnapshot Snapshot(params FrameworkObjectData[] additions)
    {
        return Snapshot(additions, [], []);
    }

    private static LevelFrameworkSnapshot Snapshot(
        IEnumerable<FrameworkObjectData> additions,
        IEnumerable<FrameworkObjectData> modifications,
        IEnumerable<FrameworkObjectData> deletions)
    {
        return new LevelFrameworkSnapshot(
            null,
            new Dictionary<string, IEnumerable<FrameworkObjectData>>
            {
                [RoomKey] = additions
            },
            new Dictionary<string, IEnumerable<FrameworkObjectData>>
            {
                [RoomKey] = modifications
            },
            new Dictionary<string, IEnumerable<FrameworkObjectData>>
            {
                [RoomKey] = deletions
            });
    }

    private static FrameworkObjectData Addition(string id)
    {
        return new FrameworkObjectData(
            null,
            null,
            "bell-iron",
            id,
            FrameworkVector.Zero,
            FrameworkVector.Zero,
            FrameworkVector.One,
            string.Empty,
            []);
    }

    private static FrameworkObjectData Modification(string path)
    {
        return new FrameworkObjectData(
            "MIDDLEGROUND",
            path,
            "decoration",
            null,
            FrameworkVector.Zero,
            FrameworkVector.Zero,
            FrameworkVector.One,
            string.Empty,
            []);
    }

    private static FrameworkObjectData Deletion(string path)
    {
        return new FrameworkObjectData(
            "MIDDLEGROUND",
            path,
            null,
            null,
            FrameworkVector.Zero,
            FrameworkVector.Zero,
            FrameworkVector.One,
            string.Empty,
            []);
    }

    private static LevelEditSession CreateSession()
    {
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(source);
        Assert.True(parsed.IsValid);
        FrameworkFileResult file = new(
            RoomKey + ".json",
            source,
            parsed.Document,
            parsed.Diagnostics);
        RoomIdentity room;
        string error;
        Assert.True(
            RoomIdentity.TryCreate(
                RoomKey,
                RoomKey + "_LOGIC",
                out room,
                out error),
            error);
        LevelEditSessionImportResult imported = LevelEditSession.Import(room, file, []);
        Assert.True(imported.Succeeded, imported.StatusMessage);
        return imported.Session;
    }

    private static string MakeDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "EditorMode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class FakeSnapshotStore : ILevelFrameworkSnapshotStore
    {
        internal FakeSnapshotStore(
            LevelFrameworkSnapshot initial,
            LevelFrameworkSnapshot reloadSnapshot)
        {
            Current = initial;
            ReloadSnapshot = reloadSnapshot;
        }

        internal LevelFrameworkSnapshot Current { get; private set; }
        internal LevelFrameworkSnapshot ReloadSnapshot { get; }
        internal bool CaptureFails { get; set; }
        internal bool ReloadFails { get; set; }
        internal bool RestoreFails { get; set; }
        internal Action OnReload { get; set; }
        internal int CaptureCalls { get; private set; }
        internal int ReloadCalls { get; private set; }
        internal int RestoreCalls { get; private set; }

        public bool TryCapture(out LevelFrameworkSnapshot snapshot, out string error)
        {
            CaptureCalls++;
            snapshot = Current;
            error = CaptureFails ? "capture failed" : null;
            return !CaptureFails;
        }

        public bool TryReload(out LevelFrameworkSnapshot snapshot, out string error)
        {
            ReloadCalls++;
            OnReload?.Invoke();
            if (ReloadFails)
            {
                snapshot = null;
                error = "reload failed";
                return false;
            }

            Current = ReloadSnapshot;
            snapshot = Current;
            error = null;
            return true;
        }

        public bool TryRestore(LevelFrameworkSnapshot snapshot, out string error)
        {
            RestoreCalls++;
            if (RestoreFails)
            {
                error = "restore failed";
                return false;
            }

            Current = snapshot;
            error = null;
            return true;
        }
    }
}