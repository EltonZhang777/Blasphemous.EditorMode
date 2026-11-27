using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class OriginalObjectReconciliationTests
{
    private const string RoomKey = "D03Z02S06";

    [Fact]
    public void Conflicting_modifications_remain_read_only_until_one_is_kept_and_history_restores_the_conflict()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"logic\",\"path\":\"LOGIC/OTHER\",\"type\":\"other\",\"id\":\"other\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"first\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"chosen\",\"id\":\"chosen\",\"position\":{\"x\":42,\"y\":2,\"z\":3}},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"extra\",\"id\":\"extra\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("other", "logic", "LOGIC/OTHER", 4f),
                Object("one", "decoration", "DECORATION/ONE", 1f, runtimeX: 42f));
            SelectOriginal(result, "decoration:DECORATION/ONE");
            OriginalObjectPage page = result.Pages.OriginalObjects;
            OriginalObjectOperation chosen = Assert.Single(
                page.Operations,
                operation => operation.Source == "modifications[2]");

            Assert.Equal(3, page.Operations.Count(operation => operation.HasConflict));
            Assert.All(
                page.Operations.Where(operation => operation.DisplayIdentity == "decoration:DECORATION/ONE"),
                operation => Assert.True(operation.HasConflict));
            Assert.False(page.TryApplySelectedModification(
                new PlacementTransform(7f, 8f, 9f, 0f, 0f, 0f, 1f, 1f, 1f),
                new ApplyReport()));
            Assert.False(page.TryDeleteSelected(new ApplyReport()));
            Assert.False(page.TryRemoveSelectedEdit(new ApplyReport()));
            Assert.Equal(4, result.Session.Modifications.Count);

            ApplyReport keepReport = new();
            Assert.True(page.TryResolveConflict(chosen, keepReport), keepReport.ToSummary("keep conflict winner"));

            Assert.Equal(
                new[] { "other", "chosen" },
                result.Session.Modifications.Select(modification => modification.Id));
            Assert.Equal("chosen", Assert.Single(projector.AppliedModifications).Id);
            Assert.DoesNotContain(page.Operations, operation => operation.HasConflict);

            LevelEditSessionExportResult exported = result.Session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
            JObject exportedJson = JObject.Parse(File.ReadAllText(path));
            Assert.Equal(
                new[] { "other", "chosen" },
                ((JArray)exportedJson["modifications"]).Select(token => (string)token["id"]));
            Assert.Empty((JArray)exportedJson["deletions"]);

            ApplyReport undoReport = new();
            Assert.True(page.TryUndo(undoReport), undoReport.ToSummary("undo conflict resolution"));
            Assert.Equal(
                new[] { "other", "first", "chosen", "extra" },
                result.Session.Modifications.Select(modification => modification.Id));
            Assert.Equal(3, page.Operations.Count(operation => operation.HasConflict));
            Assert.Equal(42f, projector.RestoredStates[projector.RestoredStates.Count - 1].Transform.PositionX);

            ApplyReport redoReport = new();
            Assert.True(page.TryRedo(redoReport), redoReport.ToSummary("redo conflict resolution"));
            Assert.Equal(
                new[] { "other", "chosen" },
                result.Session.Modifications.Select(modification => modification.Id));
            Assert.Equal(
                1f,
                projector.RestoredStates[projector.RestoredStates.Count - 1].Transform.PositionX);

            ApplyReport resetReport = new();
            Assert.True(result.Pages.TryReset(resetReport), resetReport.ToSummary("reset conflict resolution"));
            Assert.Equal(
                new[] { "other", "first", "chosen", "extra" },
                result.Session.Modifications.Select(modification => modification.Id));
            Assert.Equal(3, page.Operations.Count(operation => operation.HasConflict));
            Assert.False(page.History.CanUndo);
            Assert.False(page.History.CanRedo);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Keeping_one_conflicting_deletion_removes_competing_modifications_and_uses_the_imported_live_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"first\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"second\",\"id\":\"second\"}],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("one", "decoration", "DECORATION/ONE", 1f, runtimeActive: false));
            SelectOriginal(result, "decoration:DECORATION/ONE");
            OriginalObjectPage page = result.Pages.OriginalObjects;
            OriginalObjectOperation chosen = Assert.Single(
                page.Operations,
                operation => operation.Kind == OriginalObjectOperationKind.Deletion);

            ApplyReport report = new();
            Assert.True(page.TryResolveConflict(chosen, report), report.ToSummary("keep deletion"));

            Assert.Empty(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            DeletionApplication application = Assert.Single(projector.AppliedDeletions);
            Assert.Equal(1f, application.AuthoredState.Transform.PositionX);
            Assert.Equal("one", application.AuthoredState.Name);
            Assert.False(application.AppliedState.IsActiveSelf);
            Assert.DoesNotContain(page.Operations, operation => operation.HasConflict);

            ApplyReport undoReport = new();
            Assert.True(page.TryUndo(undoReport), undoReport.ToSummary("undo deletion conflict"));
            Assert.Equal(2, result.Session.Modifications.Count);
            Assert.Single(result.Session.Deletions);
            Assert.Equal(3, page.Operations.Count(operation => operation.HasConflict));

            ApplyReport redoReport = new();
            Assert.True(page.TryRedo(redoReport), redoReport.ToSummary("redo deletion conflict"));
            Assert.Empty(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            Assert.Equal(2, projector.AppliedDeletions.Count);
            DeletionApplication redoApplication = projector.AppliedDeletions[1];
            Assert.Equal(1f, redoApplication.AuthoredState.Transform.PositionX);
            Assert.Equal("one", redoApplication.AuthoredState.Name);
            Assert.False(redoApplication.AppliedState.IsActiveSelf);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Untouched_imported_modifications_and_deletions_are_read_only_for_remove()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"logic\",\"path\":\"LOGIC/FIRST\",\"type\":\"editor-mode-empty\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"editor-mode-empty\",\"id\":\"target\",\"position\":{\"x\":42,\"y\":2,\"z\":3}},{\"scene\":\"logic\",\"path\":\"LOGIC/LAST\",\"type\":\"editor-mode-empty\",\"id\":\"last\"}],\"deletions\":[{\"scene\":\"logic\",\"path\":\"LOGIC/DELETED\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("first", "logic", "LOGIC/FIRST", 11f),
                Object("one", "decoration", "DECORATION/ONE", 1f, runtimeX: 42f),
                Object("last", "logic", "LOGIC/LAST", 13f),
                Object("deleted", "logic", "LOGIC/DELETED", 14f));
            SelectOriginal(result, "decoration:DECORATION/ONE");

            ApplyReport removeModification = new();
            Assert.False(result.Pages.OriginalObjects.TryRemoveSelectedEdit(removeModification));
            Assert.Contains(
                removeModification.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectEditReadOnly);
            Assert.Equal(
                new[] { "first", "target", "last" },
                result.Session.Modifications.Select(modification => modification.Id));
            Assert.Single(result.Session.Deletions);
            Assert.Empty(projector.RestoredStates);

            SelectOriginal(result, "logic:LOGIC/DELETED");
            ApplyReport removeDeletion = new();
            Assert.False(result.Pages.OriginalObjects.TryRemoveSelectedEdit(removeDeletion));
            Assert.Contains(
                removeDeletion.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectEditReadOnly);
            Assert.Equal(3, result.Session.Modifications.Count);
            Assert.Single(result.Session.Deletions);
            Assert.Empty(projector.RestoredStates);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Removing_a_pending_replacement_restores_the_imported_operation_and_live_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"logic\",\"path\":\"LOGIC/FIRST\",\"type\":\"editor-mode-empty\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"editor-mode-empty\",\"id\":\"target\",\"position\":{\"x\":42,\"y\":2,\"z\":3}},{\"scene\":\"logic\",\"path\":\"LOGIC/LAST\",\"type\":\"editor-mode-empty\",\"id\":\"last\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("first", "logic", "LOGIC/FIRST", 11f),
                Object("one", "decoration", "DECORATION/ONE", 1f, runtimeX: 42f),
                Object("last", "logic", "LOGIC/LAST", 13f));
            SelectOriginal(result, "decoration:DECORATION/ONE");

            ApplyReport modifyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 0f, 0f, 0f, 1f, 1f, 1f),
                    modifyReport),
                modifyReport.ToSummary("replace imported modification"));
            Assert.Equal(
                7f,
                Assert.Single(result.Session.Modifications, operation => operation.Id == "target")
                    .Position.X);

            ApplyReport removeReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRemoveSelectedEdit(removeReport),
                removeReport.ToSummary("remove pending replacement"));

            Assert.Equal(42f, Assert.Single(
                result.Session.Modifications,
                operation => operation.Id == "target").Position.X);
            Assert.Equal(
                new[] { "first", "target", "last" },
                result.Session.Modifications.Select(modification => modification.Id));
            Assert.Equal(
                42f,
                projector.RestoredStates[projector.RestoredStates.Count - 1].Transform.PositionX);

            LevelEditSessionExportResult exported = result.Session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, RoomKey);
            Assert.Equal(
                new[] { "first", "target", "last" },
                file.Document.Modifications.Select(modification => modification.Id));
            Assert.Equal(
                42f,
                Assert.Single(file.Document.Modifications, operation => operation.Id == "target")
                    .Position.X);

            ApplyReport undoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoReport),
                undoReport.ToSummary("undo pending replacement removal"));
            Assert.Equal(
                7f,
                Assert.Single(result.Session.Modifications, operation => operation.Id == "target")
                    .Position.X);
            Assert.Equal(7f, projector.AppliedModifications[projector.AppliedModifications.Count - 1].Position.X);

            ApplyReport redoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRedo(redoReport),
                redoReport.ToSummary("redo pending replacement removal"));
            Assert.Equal(
                42f,
                Assert.Single(result.Session.Modifications, operation => operation.Id == "target")
                    .Position.X);
            Assert.Equal(
                42f,
                projector.AppliedModifications[projector.AppliedModifications.Count - 1].Position.X);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Removing_a_pending_replacement_after_conflict_resolution_restores_the_kept_imported_operation()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"first\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"chosen\",\"id\":\"chosen\",\"position\":{\"x\":42,\"y\":2,\"z\":3}},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"extra\",\"id\":\"extra\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("one", "decoration", "DECORATION/ONE", 1f, runtimeX: 42f));
            SelectOriginal(result, "decoration:DECORATION/ONE");
            OriginalObjectPage page = result.Pages.OriginalObjects;
            OriginalObjectOperation chosen = Assert.Single(
                page.Operations,
                operation => operation.Id == "chosen");

            ApplyReport keepReport = new();
            Assert.True(page.TryResolveConflict(chosen, keepReport), keepReport.ToSummary("keep conflict winner"));
            ApplyReport undoReport = new();
            Assert.True(page.TryUndo(undoReport), undoReport.ToSummary("undo conflict resolution"));
            ApplyReport redoReport = new();
            Assert.True(page.TryRedo(redoReport), redoReport.ToSummary("redo conflict resolution"));

            ApplyReport modifyReport = new();
            Assert.True(
                page.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 0f, 0f, 0f, 1f, 1f, 1f),
                    modifyReport),
                modifyReport.ToSummary("modify resolved import"));

            ApplyReport removeReport = new();
            Assert.True(
                page.TryRemoveSelectedEdit(removeReport),
                removeReport.ToSummary("remove resolved import replacement"));

            FrameworkObjectData restored = Assert.Single(result.Session.Modifications);
            Assert.Equal("chosen", restored.Id);
            Assert.Equal(42f, restored.Position.X);
            Assert.DoesNotContain(page.Operations, operation => operation.HasConflict);
            Assert.Equal(
                42f,
                projector.RestoredStates[projector.RestoredStates.Count - 1].Transform.PositionX);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Removing_a_pending_new_operation_restores_the_authored_baseline_and_updates_export_history()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"logic\",\"path\":\"LOGIC/FIRST\",\"type\":\"editor-mode-empty\",\"id\":\"first\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("first", "logic", "LOGIC/FIRST", 11f),
                Object("one", "decoration", "DECORATION/ONE", 1f));
            SelectOriginal(result, "decoration:DECORATION/ONE");

            ApplyReport modifyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(5f, 2f, 3f, 0f, 0f, 0f, 1f, 1f, 1f),
                    modifyReport),
                modifyReport.ToSummary("apply new modification"));
            Assert.Equal(2, result.Session.Modifications.Count);

            ApplyReport removeReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRemoveSelectedEdit(removeReport),
                removeReport.ToSummary("remove new modification"));

            Assert.Equal("first", Assert.Single(result.Session.Modifications).Id);
            OriginalObjectLiveState authored = projector.RestoredStates[projector.RestoredStates.Count - 1];
            Assert.True(authored.IsActiveSelf);
            Assert.Equal(1f, authored.Transform.PositionX);
            Assert.Equal("one", authored.Name);

            LevelEditSessionExportResult exported = result.Session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, RoomKey);
            Assert.Equal("first", Assert.Single(file.Document.Modifications).Id);
            Assert.Empty(file.Document.Deletions);

            ApplyReport undoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoReport),
                undoReport.ToSummary("undo new removal"));
            Assert.Equal(2, result.Session.Modifications.Count);
            Assert.Equal(5f, projector.AppliedModifications[projector.AppliedModifications.Count - 1].Position.X);

            ApplyReport redoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRedo(redoReport),
                redoReport.ToSummary("redo new removal"));
            Assert.Single(result.Session.Modifications);
            Assert.Equal(
                1f,
                projector.RestoredStates[projector.RestoredStates.Count - 1].Transform.PositionX);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Two_imported_deletions_remain_a_read_only_conflict_until_one_is_kept()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("one", "decoration", "DECORATION/ONE", 1f));
            SelectOriginal(result, "decoration:DECORATION/ONE");
            OriginalObjectPage page = result.Pages.OriginalObjects;

            Assert.All(page.Operations, operation => Assert.True(operation.HasConflict));
            Assert.False(page.TryRemoveSelectedEdit(new ApplyReport()));
            Assert.Equal(2, result.Session.Deletions.Count);

            ApplyReport keepReport = new();
            Assert.True(
                page.TryResolveConflict(page.Operations[0], keepReport),
                keepReport.ToSummary("keep one deletion"));
            Assert.Single(result.Session.Deletions);
            Assert.DoesNotContain(page.Operations, operation => operation.HasConflict);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Unlocatable_parseable_operation_remains_visible_read_only_and_cannot_be_removed()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/MISSING\",\"type\":\"unknown\",\"id\":\"missing\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("present", "decoration", "DECORATION/PRESENT", 1f));

            ApplyReport switchReport = new();
            Assert.True(result.Pages.TrySwitchPage(switchReport), switchReport.ToSummary("switch to Original"));
            OriginalObjectOperation operation = Assert.Single(result.Pages.OriginalObjects.Operations);
            Assert.False(operation.IsLocatable);
            Assert.True(operation.IsReadOnly);
            Assert.Contains(
                result.Report.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectOperationUnlocatable);

            ApplyReport selectReport = new();
            Assert.False(result.Pages.TrySelectOriginal(operation.DisplayIdentity, selectReport, out _));
            Assert.Contains(
                selectReport.Entries,
                entry => entry.Code == ReportCodes.SelectionCandidateNotFound);

            ApplyReport removeReport = new();
            Assert.False(result.Pages.OriginalObjects.TryRemoveSelectedEdit(removeReport));
            Assert.Contains(
                removeReport.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectEditNotFound);
            Assert.Single(result.Session.Modifications);

            LevelEditSessionExportResult exported = result.Session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, RoomKey);
            Assert.Equal("missing", Assert.Single(file.Document.Modifications).Id);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Reset_restores_the_imported_conflict_and_clears_both_page_histories()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"addition\"},{\"type\":\"bell-iron\",\"id\":\"malformed\",\"properties\":\"not-an-array\"}],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"first\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"chosen\",\"id\":\"chosen\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("one", "decoration", "DECORATION/ONE", 1f));
            SelectOriginal(result, "decoration:DECORATION/ONE");
            OriginalObjectOperation chosen = Assert.Single(
                result.Pages.OriginalObjects.Operations,
                operation => operation.Id == "chosen");
            ApplyReport keepReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryResolveConflict(chosen, keepReport),
                keepReport.ToSummary("keep conflict winner"));
            ApplyReport moveReport = new();
            Assert.True(
                result.Pages.Additions.TryMove(
                    "addition",
                    new PlacementTransform(5f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
                    moveReport),
                moveReport.ToSummary("move addition"));
            Assert.True(result.Pages.OriginalObjects.History.CanUndo);
            Assert.True(result.Pages.Additions.History.CanUndo);

            ApplyReport resetReport = new();
            Assert.True(result.Pages.TryReset(resetReport), resetReport.ToSummary("reset"));

            Assert.Equal(result.Session.ImportedState, result.Session.State);
            Assert.Equal(LevelEditSessionState.Partial, result.Session.State);
            Assert.Equal(2, result.Session.Modifications.Count);
            Assert.Equal(2, result.Pages.OriginalObjects.Operations.Count(operation => operation.HasConflict));
            Assert.False(result.Pages.OriginalObjects.History.CanUndo);
            Assert.False(result.Pages.OriginalObjects.History.CanRedo);
            Assert.False(result.Pages.Additions.History.CanUndo);
            Assert.False(result.Pages.Additions.History.CanRedo);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Reset_preserves_partial_import_state_for_a_conflict_only_import()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"first\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"chosen\",\"id\":\"chosen\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("one", "decoration", "DECORATION/ONE", 1f));
            Assert.True(result.Report.IsPartial, result.StatusMessage);
            Assert.Equal(LevelEditSessionState.Partial, result.Session.ImportedState);

            ApplyReport resetReport = new();
            Assert.True(result.Pages.TryReset(resetReport), resetReport.ToSummary("reset conflict"));
            Assert.Equal(LevelEditSessionState.Partial, result.Session.State);
            Assert.Equal(LevelEditSessionState.Partial, result.Session.ImportedState);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Keeping_a_conflicting_modification_restores_the_authored_live_baseline_before_applying_it()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"first\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"chosen\",\"id\":\"chosen\",\"position\":{\"x\":42,\"y\":2,\"z\":3}}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Object("one", "decoration", "DECORATION/ONE", 1f, runtimeX: 42f));
            SelectOriginal(result, "decoration:DECORATION/ONE");

            ApplyReport keepReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryResolveConflict(
                    Assert.Single(
                        result.Pages.OriginalObjects.Operations,
                        operation => operation.Id == "chosen"),
                    keepReport),
                keepReport.ToSummary("keep conflict winner"));

            OriginalObjectLiveState restored = Assert.Single(projector.RestoredStates);
            Assert.Equal(1f, restored.Transform.PositionX);
            Assert.Equal("one", restored.Name);
            Assert.Equal(42f, Assert.Single(projector.AppliedModifications).Position.X);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Full_three_array_round_trip_preserves_order_identity_and_unedited_fields()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, RoomKey + ".json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"add-1\",\"position\":{\"x\":1.5,\"y\":2.5,\"z\":3.5},\"rotation\":{\"x\":4.5,\"y\":5.5,\"z\":6.5},\"scale\":{\"x\":2.0,\"y\":3.0,\"z\":4.0},\"condition\":\"flag:one\",\"properties\":[\"first=1\",\"second=2\"]},{\"type\":\"unknown-creator\",\"id\":\"add-2\",\"position\":{\"x\":7.0,\"y\":8.0,\"z\":9.0},\"rotation\":{\"x\":10.0,\"y\":11.0,\"z\":12.0},\"scale\":{\"x\":2.0,\"y\":2.0,\"z\":2.0},\"condition\":\"flag:two\",\"properties\":[\"opaque=yes\"],\"futureField\":{\"keep\":true}}],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"first-modifier\",\"id\":\"mod-1\",\"position\":{\"x\":1.0,\"y\":2.0,\"z\":3.0},\"rotation\":{\"x\":4.0,\"y\":5.0,\"z\":6.0},\"scale\":{\"x\":2.0,\"y\":3.0,\"z\":4.0},\"condition\":\"flag:first\",\"properties\":[\"one=1\",\"shared=1\"]},{\"scene\":\"decoration\",\"path\":\"DECORATION/ONE\",\"type\":\"second-modifier\",\"id\":\"mod-2\",\"position\":{\"x\":7.0,\"y\":8.0,\"z\":9.0},\"rotation\":{\"x\":10.0,\"y\":11.0,\"z\":12.0},\"scale\":{\"x\":5.0,\"y\":6.0,\"z\":7.0},\"condition\":\"flag:second\",\"properties\":[\"two=2\",\"shared=2\"]},{\"scene\":\"logic\",\"path\":\"LOGIC/OTHER\",\"type\":\"third-modifier\",\"id\":\"mod-3\",\"position\":{\"x\":13.0,\"y\":14.0,\"z\":15.0},\"rotation\":{\"x\":16.0,\"y\":17.0,\"z\":18.0},\"scale\":{\"x\":8.0,\"y\":9.0,\"z\":10.0},\"condition\":\"flag:third\",\"properties\":[]}],\"deletions\":[{\"scene\":\"layout\",\"path\":\"LAYOUT/ONE\"},{\"scene\":\"logic\",\"path\":\"LOGIC/TWO\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, RoomKey);
            LevelEditSessionImportResult imported = LevelEditSession.Import(
                CreateRoom(),
                file,
                new[] { BaseCreatorCatalog.BellIron });
            Assert.True(imported.Succeeded, imported.StatusMessage);

            LevelEditSessionExportResult exported = imported.Session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
            JObject originalJson = JObject.Parse(source);
            JObject exportedJson = JObject.Parse(File.ReadAllText(path));
            Assert.True(
                JToken.DeepEquals(originalJson["additions"], exportedJson["additions"]),
                exportedJson["additions"].ToString());
            Assert.True(
                JToken.DeepEquals(originalJson["modifications"], exportedJson["modifications"]),
                exportedJson["modifications"].ToString());
            Assert.True(
                JToken.DeepEquals(originalJson["deletions"], exportedJson["deletions"]),
                exportedJson["deletions"].ToString());

            FrameworkFileResult reimportedFile = LevelFrameworkAdapter.ImportFile(path, RoomKey);
            LevelEditSessionImportResult reimported = LevelEditSession.Import(
                CreateRoom(),
                reimportedFile,
                new[] { BaseCreatorCatalog.BellIron });
            Assert.True(reimported.Succeeded, reimported.StatusMessage);
            Assert.Equal(
                new[] { "add-1", "add-2" },
                reimportedFile.Document.Additions.Select(addition => addition.Id));
            Assert.Equal(
                new[] { "mod-1", "mod-2", "mod-3" },
                reimported.Session.Modifications.Select(modification => modification.Id));
            Assert.Equal(
                new[]
                {
                    "layout:LAYOUT/ONE",
                    "logic:LOGIC/TWO"
                },
                reimported.Session.Deletions.Select(deletion => deletion.Scene + ":" + deletion.Path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static RoomOverlayReconcileResult Reconcile(
        string path,
        string source,
        FakeOriginalObjectProjector projector,
        params OriginalObjectFixture[] originals)
    {
        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(source);
        Assert.NotNull(parsed.Document);
        FrameworkFileResult file = new(path, source, parsed.Document, parsed.Diagnostics);
        OriginalObjectBaseline baseline = new(
            RoomKey,
            originals.Select(original => original.Snapshot),
            new ApplyReport());
        FakeOriginalObjectPathResolver resolver = new();
        foreach (OriginalObjectFixture original in originals)
            resolver.Add(original);

        RoomOverlayReconciler reconciler = new(
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            new FakeRuntimeFactory(),
            projector);
        RoomOverlayReconcileResult result = reconciler.Apply(
            CreateRoom(),
            file,
            baseline,
            resolver);
        Assert.True(result.Succeeded, result.StatusMessage);
        return result;
    }

    private static void SelectOriginal(RoomOverlayReconcileResult result, string identity)
    {
        ApplyReport report = new();
        if (result.Pages.ActivePage != EditorPage.OriginalObjects)
            Assert.True(result.Pages.TrySwitchPage(report), report.ToSummary("switch to Original"));
        Assert.True(result.Pages.TrySelectOriginal(identity, report, out _), report.ToSummary("select Original"));
    }

    private static OriginalObjectFixture Object(
        string objectKey,
        string scene,
        string path,
        float authoredX,
        float runtimeX = 0f,
        bool? runtimeActive = null)
    {
        PlacementTransform authored = Transform(authoredX);
        PlacementTransform runtime = Transform(runtimeX == 0f ? authoredX : runtimeX);
        OriginalObjectSnapshotEntry snapshot = new(
            objectKey,
            scene,
            path,
            true,
            OriginalObjectProvenance.Baseline,
            true,
            authoredTransform: authored,
            authoredName: objectKey);
        return new OriginalObjectFixture(
            snapshot,
            runtime,
            runtimeActive ?? true,
            objectKey);
    }

    private static PlacementTransform Transform(float x)
    {
        return new PlacementTransform(
            x,
            2f,
            3f,
            0f,
            0f,
            0f,
            1f,
            1f,
            1f);
    }

    private static RoomIdentity CreateRoom()
    {
        RoomIdentity room;
        string error;
        Assert.True(
            RoomIdentity.TryCreate(RoomKey, RoomKey + "_LOGIC", out room, out error),
            error);
        return room;
    }

    private static string MakeDirectory()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "editor-mode-original-reconciliation-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        string disposedRoot = Path.Combine(Path.GetTempPath(), "editor-mode-test-disposed");
        Directory.CreateDirectory(disposedRoot);
        Directory.Move(
            directory,
            Path.Combine(
                disposedRoot,
                Path.GetFileName(directory) + "-" + Guid.NewGuid().ToString("N")));
    }

    private sealed class OriginalObjectFixture
    {
        internal OriginalObjectFixture(
            OriginalObjectSnapshotEntry snapshot,
            PlacementTransform runtimeTransform,
            bool runtimeActive,
            string runtimeName)
        {
            Snapshot = snapshot;
            RuntimeTransform = runtimeTransform;
            RuntimeActive = runtimeActive;
            RuntimeName = runtimeName;
        }

        internal OriginalObjectSnapshotEntry Snapshot { get; }
        internal PlacementTransform RuntimeTransform { get; }
        internal bool RuntimeActive { get; }
        internal string RuntimeName { get; }
    }

    private sealed class FakeOriginalObjectPathResolver : IOriginalObjectPathResolver
    {
        private readonly Dictionary<string, OriginalObjectProjection> _projections = new(StringComparer.Ordinal);

        internal void Add(OriginalObjectFixture original)
        {
            _projections[Key(original.Snapshot.Scene, original.Snapshot.Path)] =
                new OriginalObjectProjection(
                    original.Snapshot.ObjectKey,
                    original.RuntimeActive,
                    original.RuntimeTransform,
                    original.RuntimeName,
                    original.RuntimeActive);
        }

        public bool TryResolve(
            string frameworkLevelKey,
            string scene,
            string path,
            out OriginalObjectProjection projection,
            out string error)
        {
            error = null;
            return _projections.TryGetValue(Key(scene, path), out projection);
        }

        private static string Key(string scene, string path)
        {
            return scene + "\n" + path;
        }
    }

    private sealed class FakeOriginalObjectProjector : IOriginalObjectLiveProjector
    {
        internal List<FrameworkObjectData> AppliedModifications { get; } = [];
        internal List<DeletionApplication> AppliedDeletions { get; } = [];
        internal List<OriginalObjectLiveState> RestoredStates { get; } = [];

        public bool IsModifierAvailable(string type)
        {
            return true;
        }

        public bool TryApplyModification(
            string frameworkLevelKey,
            FrameworkObjectData modification,
            ApplyReport report,
            out OriginalObjectLiveState appliedState)
        {
            AppliedModifications.Add(modification);
            appliedState = new OriginalObjectLiveState(
                string.Empty,
                true,
                PlacementTransform.FromFramework(modification),
                string.Empty);
            return true;
        }

        public bool TryApplyDeletion(
            string frameworkLevelKey,
            OriginalObjectIdentity identity,
            OriginalObjectLiveState authoredState,
            ApplyReport report,
            out OriginalObjectLiveState appliedState)
        {
            appliedState = new OriginalObjectLiveState(
                authoredState.ObjectKey,
                false,
                authoredState.Transform,
                authoredState.Name);
            AppliedDeletions.Add(new DeletionApplication(authoredState, appliedState));
            return true;
        }

        public bool TryRestore(
            string frameworkLevelKey,
            OriginalObjectIdentity identity,
            OriginalObjectLiveState state,
            ApplyReport report)
        {
            RestoredStates.Add(state);
            return true;
        }
    }

    private sealed class DeletionApplication
    {
        internal DeletionApplication(
            OriginalObjectLiveState authoredState,
            OriginalObjectLiveState appliedState)
        {
            AuthoredState = authoredState;
            AppliedState = appliedState;
        }

        internal OriginalObjectLiveState AuthoredState { get; }
        internal OriginalObjectLiveState AppliedState { get; }
    }

    private sealed class FakeRuntimeFactory : IRuntimePlacementFactory
    {
        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            return new FakeRuntimeHandle(placement.Id);
        }
    }

    private sealed class FakeRuntimeHandle : IRuntimePlacementHandle
    {
        internal FakeRuntimeHandle(string placementId)
        {
            PlacementId = placementId;
            IsAlive = true;
        }

        public string PlacementId { get; }
        public bool IsOriginal { get { return false; } }
        public bool IsAlive { get; private set; }

        public bool TryApply(PlacementRecord placement, out string error)
        {
            error = null;
            return IsAlive;
        }

        public bool TryDestroy(out string error)
        {
            error = null;
            IsAlive = false;
            return true;
        }
    }
}