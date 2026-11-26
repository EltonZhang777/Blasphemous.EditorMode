using Blasphemous.EditorMode.Content;
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

public sealed class OriginalObjectEditingTests
{
    [Fact]
    public void Apply_modify_creates_editor_mode_empty_operation_without_writing_source()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport report = new();
            bool applied = result.Pages.OriginalObjects.TryApplySelectedModification(
                new PlacementTransform(7f, 8f, 9f, 10f, 11f, 12f, 2f, 3f, 4f),
                report);

            Assert.True(applied, report.ToSummary("apply modify"));
            FrameworkObjectData modification = Assert.Single(result.Session.Modifications);
            Assert.Equal("decoration", modification.Scene);
            Assert.Equal("DECORATION/HERO", modification.Path);
            Assert.Equal(OriginalObjectEditor.EmptyModificationType, modification.Type);
            Assert.Equal("editor-mode-empty-1", modification.Id);
            Assert.Equal(string.Empty, modification.Condition);
            Assert.Empty(modification.Properties);
            Assert.Equal(7f, modification.Position.X);
            Assert.Equal(11f, modification.Rotation.Y);
            Assert.Equal(4f, modification.Scale.Z);
            Assert.Single(projector.AppliedModifications);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Apply_modify_rejects_missing_or_nonfinite_transforms()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            PlacementTransform[] invalid =
            [
                null,
                new PlacementTransform(
                    float.NaN,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    1f,
                    1f,
                    1f)
            ];

            foreach (PlacementTransform transform in invalid)
            {
                ApplyReport report = new();
                Assert.False(
                    result.Pages.OriginalObjects.TryApplySelectedModification(
                        transform,
                        report));
                Assert.Contains(
                    report.Entries,
                    entry => entry.Code == ReportCodes.TransformInvalid);
            }

            Assert.Empty(result.Session.Modifications);
            Assert.Empty(projector.AppliedModifications);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Apply_modify_upserts_and_preserves_imported_fields()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"custom-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"rotation\":{\"x\":4,\"y\":5,\"z\":6},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"flag:opened\",\"properties\":[\"a=1\",\"a=2\"]}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport firstReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 10f, 11f, 12f, 2f, 3f, 4f),
                    firstReport),
                firstReport.ToSummary("first modify"));
            ApplyReport secondReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(17f, 18f, 19f, 20f, 21f, 22f, 5f, 6f, 7f),
                    secondReport),
                secondReport.ToSummary("second modify"));

            FrameworkObjectData modification = Assert.Single(result.Session.Modifications);
            Assert.Equal("custom-modifier", modification.Type);
            Assert.Equal("imported-id", modification.Id);
            Assert.Equal("flag:opened", modification.Condition);
            Assert.Equal(new[] { "a=1", "a=2" }, modification.Properties);
            Assert.Equal(17f, modification.Position.X);
            Assert.Equal(22f, modification.Rotation.Z);
            Assert.Equal(7f, modification.Scale.Z);
            Assert.Equal(2, projector.AppliedModifications.Count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_modifier_application_leaves_session_and_source_uncommitted()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new()
            {
                FailModification = true
            };
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            LevelEditSessionState stateBefore = result.Session.State;

            ApplyReport report = new();
            bool applied = result.Pages.OriginalObjects.TryApplySelectedModification(
                new PlacementTransform(7f, 8f, 9f, 10f, 11f, 12f, 2f, 3f, 4f),
                report);

            Assert.False(applied);
            Assert.Empty(result.Session.Modifications);
            Assert.Equal(stateBefore, result.Session.State);
            Assert.False(result.Pages.OriginalObjects.History.CanUndo);
            Assert.Contains(
                report.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectModifierFailed);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Delete_selected_original_deactivates_and_exports_only_scene_and_path()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete"));

            Assert.Empty(result.Session.Modifications);
            FrameworkObjectData deletion = Assert.Single(result.Session.Deletions);
            Assert.Equal("decoration", deletion.Scene);
            Assert.Equal("DECORATION/HERO", deletion.Path);
            Assert.Null(deletion.Type);
            Assert.Null(deletion.Id);
            Assert.Equal(string.Empty, deletion.Condition);
            Assert.Empty(deletion.Properties);
            Assert.True(result.Pages.OriginalObjects.SelectedHasDeletion);
            OriginalObjectIdentity deleted = Assert.Single(projector.AppliedDeletions);
            Assert.Equal("decoration", deleted.Scene);
            Assert.False(projector.LastAppliedState.IsActiveSelf);
            Assert.Equal(1f, projector.LastAppliedState.Transform.PositionX);
            Assert.Equal(source, File.ReadAllText(path));

            LevelEditSessionExportResult export = result.Session.Export(path);
            Assert.True(export.Succeeded, export.StatusMessage);
            JObject root = JObject.Parse(File.ReadAllText(path));
            JObject exportedDeletion = (JObject)root["deletions"][0];
            Assert.Equal(2, exportedDeletion.Count);
            Assert.Equal("decoration", exportedDeletion["scene"].Value<string>());
            Assert.Equal("DECORATION/HERO", exportedDeletion["path"].Value<string>());

            FrameworkFileResult reimported = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            Assert.True(reimported.IsValid);
            LevelEditSessionImportResult imported = LevelEditSession.Import(
                CreateRoom(),
                reimported,
                new[] { BaseCreatorCatalog.BellIron });
            FrameworkObjectData roundTrip = Assert.Single(imported.Session.Deletions);
            Assert.Equal("decoration", roundTrip.Scene);
            Assert.Equal("DECORATION/HERO", roundTrip.Path);
            Assert.Null(roundTrip.Type);
            Assert.Null(roundTrip.Id);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Delete_and_modify_replace_each_other_and_restore_active_state_through_history()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport modifyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
                    modifyReport),
                modifyReport.ToSummary("initial modify"));
            Assert.Single(result.Session.Modifications);

            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete modification"));
            Assert.Empty(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            Assert.False(projector.LastAppliedState.IsActiveSelf);
            Assert.Equal(1f, projector.LastAppliedState.Transform.PositionX);

            ApplyReport undoDelete = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoDelete),
                undoDelete.ToSummary("undo delete"));
            Assert.Equal(7f, Assert.Single(result.Session.Modifications).Position.X);
            Assert.Empty(result.Session.Deletions);
            Assert.True(projector.LastAppliedState.IsActiveSelf);

            ApplyReport secondDelete = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(secondDelete),
                secondDelete.ToSummary("delete again"));
            Assert.Single(result.Session.Deletions);
            Assert.Empty(result.Session.Modifications);

            ApplyReport modifyOverDeletion = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(11f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
                    modifyOverDeletion),
                modifyOverDeletion.ToSummary("modify over deletion"));
            Assert.Equal(11f, Assert.Single(result.Session.Modifications).Position.X);
            Assert.Empty(result.Session.Deletions);
            Assert.True(projector.LastAppliedState.IsActiveSelf);
            Assert.NotEmpty(projector.RestoredStates);

            ApplyReport undoModify = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoModify),
                undoModify.ToSummary("undo modify over deletion"));
            Assert.Single(result.Session.Deletions);
            Assert.Empty(result.Session.Modifications);
            Assert.False(projector.LastAppliedState.IsActiveSelf);

            ApplyReport undoSecondDelete = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoSecondDelete),
                undoSecondDelete.ToSummary("undo second delete"));
            Assert.Equal(7f, Assert.Single(result.Session.Modifications).Position.X);
            Assert.Empty(result.Session.Deletions);
            Assert.True(projector.LastAppliedState.IsActiveSelf);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Deletion_preview_survives_editor_close_and_reopen_and_reset_restores_active_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete"));

            EditorRuntimeState runtime = CreateRuntimeState();
            EditorModeGuard guard = new(
                () => runtime,
                value => runtime = value);
            Assert.True(
                guard.TryEnter(true, true, CreateRuntimeState(), out string error),
                error);
            Assert.True(guard.TryExit(out error), error);
            Assert.True(
                guard.TryEnter(true, true, CreateRuntimeState(), out error),
                error);

            Assert.Single(result.Session.Deletions);
            Assert.True(result.Pages.OriginalObjects.SelectedHasDeletion);
            Assert.Empty(projector.RestoredStates);
            Assert.True(guard.TryExit(out error), error);

            ApplyReport resetReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryReset(resetReport),
                resetReport.ToSummary("reset"));

            Assert.Empty(result.Session.Deletions);
            Assert.Equal(LevelEditSessionState.Clean, result.Session.State);
            Assert.False(result.Pages.OriginalObjects.History.CanUndo);
            OriginalObjectLiveState restored = Assert.Single(projector.RestoredStates);
            Assert.True(restored.IsActiveSelf);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Deletion_preview_survives_page_switch_and_export()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete"));

            ApplyReport pageReport = new();
            Assert.True(result.Pages.TrySwitchPage(pageReport));
            Assert.True(result.Pages.TrySwitchPage(pageReport));
            Assert.Single(result.Session.Deletions);
            Assert.True(result.Pages.OriginalObjects.SelectedHasDeletion);
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
            Assert.Single(projector.AppliedDeletions);
            Assert.Empty(projector.RestoredStates);

            LevelEditSessionExportResult export = result.Session.Export(path);
            Assert.True(export.Succeeded, export.StatusMessage);
            Assert.Single(result.Session.Deletions);
            Assert.True(result.Pages.OriginalObjects.SelectedHasDeletion);
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
            Assert.Single(projector.AppliedDeletions);
            Assert.Empty(projector.RestoredStates);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Deletion_history_redo_deactivates_the_object_and_undo_reactivates_it()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            Assert.True(result.Pages.OriginalObjects.SelectedIsRuntimeActive);

            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete"));
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);

            ApplyReport undoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoReport),
                undoReport.ToSummary("undo delete"));
            Assert.Empty(result.Session.Deletions);
            Assert.True(result.Pages.OriginalObjects.SelectedIsRuntimeActive);

            ApplyReport redoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRedo(redoReport),
                redoReport.ToSummary("redo delete"));
            Assert.Single(result.Session.Deletions);
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
            Assert.Equal(2, projector.AppliedDeletions.Count);
            Assert.Equal(1f, projector.LastAppliedState.Transform.PositionX);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Modify_delete_undo_redo_restores_the_imported_transform_and_active_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport modifyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
                    modifyReport),
                modifyReport.ToSummary("modify"));

            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete"));
            Assert.False(projector.LastAppliedState.IsActiveSelf);
            Assert.Equal(1f, projector.LastAppliedState.Transform.PositionX);

            ApplyReport undoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoReport),
                undoReport.ToSummary("undo delete"));
            Assert.Equal(7f, Assert.Single(result.Session.Modifications).Position.X);
            Assert.Empty(result.Session.Deletions);
            Assert.True(projector.LastAppliedState.IsActiveSelf);
            Assert.Equal(7f, projector.LastAppliedState.Transform.PositionX);

            ApplyReport redoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRedo(redoReport),
                redoReport.ToSummary("redo delete"));
            Assert.Empty(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
            Assert.False(projector.LastAppliedState.IsActiveSelf);
            Assert.Equal(1f, projector.LastAppliedState.Transform.PositionX);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_deletion_application_keeps_session_and_source_uncommitted()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new()
            {
                FailDeletion = true
            };
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            LevelEditSessionState stateBefore = result.Session.State;

            ApplyReport report = new();
            Assert.False(result.Pages.OriginalObjects.TryDeleteSelected(report));

            Assert.Empty(result.Session.Deletions);
            Assert.Equal(stateBefore, result.Session.State);
            Assert.False(result.Pages.OriginalObjects.History.CanUndo);
            Assert.Contains(
                report.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectApplyFailed);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_deletion_restore_is_itemized_and_keeps_the_deletion()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete"));
            projector.FailRestore = true;

            ApplyReport undoReport = new();
            Assert.False(result.Pages.OriginalObjects.TryUndo(undoReport));

            Assert.Single(result.Session.Deletions);
            Assert.True(result.Pages.OriginalObjects.History.CanUndo);
            Assert.Contains(
                undoReport.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectLiveRestoreFailed);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_modification_over_an_imported_deletion_restores_the_inactive_preview()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new()
            {
                FailModification = true
            };
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                active: false,
                selfActive: false,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport report = new();
            Assert.False(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    report));

            Assert.Empty(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            Assert.True(Assert.Single(projector.RestoredStates).IsActiveSelf);
            Assert.Single(projector.AppliedDeletions);
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
            Assert.DoesNotContain(
                report.Entries,
                entry => entry.Code == ReportCodes.HistoryRollbackFailed);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_modification_over_an_imported_deletion_reports_a_failed_live_rollback()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new()
            {
                FailModification = true,
                FailDeletion = true
            };
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                active: false,
                selfActive: false,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport report = new();
            Assert.False(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    report));

            Assert.Empty(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            Assert.Contains(
                report.Entries,
                entry => entry.Code == ReportCodes.HistoryRollbackFailed);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_history_restore_reports_a_failed_live_rollback()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport modifyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    modifyReport),
                modifyReport.ToSummary("modify"));
            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete"));
            projector.FailModification = true;
            projector.FailDeletion = true;

            ApplyReport undoReport = new();
            Assert.False(result.Pages.OriginalObjects.TryUndo(undoReport));

            Assert.Empty(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            Assert.True(result.Pages.OriginalObjects.History.CanUndo);
            Assert.Contains(
                undoReport.Entries,
                entry => entry.Code == ReportCodes.HistoryRollbackFailed);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Removing_a_pending_deletion_restores_active_state_and_can_be_undone()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete"));

            ApplyReport removeReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRemoveSelectedEdit(removeReport),
                removeReport.ToSummary("remove deletion"));
            Assert.Empty(result.Session.Deletions);
            Assert.Empty(result.Session.Modifications);
            Assert.True(Assert.Single(projector.RestoredStates).IsActiveSelf);

            ApplyReport undoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoReport),
                undoReport.ToSummary("undo removal"));
            Assert.Single(result.Session.Deletions);
            Assert.False(projector.LastAppliedState.IsActiveSelf);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Deletion_preview_ends_at_teardown_and_restores_active_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete"));

            ApplyReport teardownReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryTeardown(teardownReport),
                teardownReport.ToSummary("teardown"));

            Assert.Null(result.Pages.OriginalObjects.Selected);
            Assert.False(result.Pages.OriginalObjects.History.CanUndo);
            Assert.True(Assert.Single(projector.RestoredStates).IsActiveSelf);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Original_history_undo_and_redo_restore_session_and_live_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport applyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 10f, 11f, 12f, 2f, 3f, 4f),
                    applyReport),
                applyReport.ToSummary("modify"));

            ApplyReport undoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoReport),
                undoReport.ToSummary("undo"));
            Assert.Empty(result.Session.Modifications);
            Assert.Equal(LevelEditSessionState.Clean, result.Session.State);
            Assert.Single(projector.RestoredStates);
            Assert.Equal(1f, projector.RestoredStates[0].Transform.PositionX);

            ApplyReport redoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRedo(redoReport),
                redoReport.ToSummary("redo"));
            FrameworkObjectData restored = Assert.Single(result.Session.Modifications);
            Assert.Equal(7f, restored.Position.X);
            Assert.Equal(LevelEditSessionState.Dirty, result.Session.State);
            Assert.Equal(2, projector.AppliedModifications.Count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Modification_ids_are_session_unique_even_after_remove_and_reapply()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("first", "decoration", "DECORATION/FIRST", true, 1f, 2f, 3f),
                Entry("second", "logic", "LOGIC/SECOND", true, 4f, 5f, 6f));

            SelectOriginal(result, "decoration:DECORATION/FIRST");
            ApplyReport firstReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(10f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
                    firstReport),
                firstReport.ToSummary("first modify"));
            Assert.Equal("editor-mode-empty-1", result.Session.Modifications[0].Id);

            SelectOriginal(result, "logic:LOGIC/SECOND");
            ApplyReport secondReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(20f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
                    secondReport),
                secondReport.ToSummary("second modify"));
            Assert.Equal("editor-mode-empty-2", result.Session.Modifications[1].Id);

            SelectOriginal(result, "decoration:DECORATION/FIRST");
            ApplyReport removeReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRemoveSelectedEdit(removeReport),
                removeReport.ToSummary("remove"));
            ApplyReport reapplyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(30f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
                    reapplyReport),
                reapplyReport.ToSummary("reapply"));

            Assert.Equal(
                new[] { "editor-mode-empty-2", "editor-mode-empty-3" },
                new[]
                {
                    result.Session.Modifications[0].Id,
                    result.Session.Modifications[1].Id
                });
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Generated_modification_id_avoids_an_existing_addition_id()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"unknown\",\"id\":\"editor-mode-empty-1\"}],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport report = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 0f, 0f, 0f, 1f, 1f, 1f),
                    report),
                report.ToSummary("modify"));

            Assert.Equal("editor-mode-empty-2", Assert.Single(result.Session.Modifications).Id);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Imported_identity_conflict_is_rejected_without_committing_or_projecting()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"first\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"second\",\"id\":\"second\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            LevelEditSessionState before = result.Session.State;

            ApplyReport report = new();
            bool applied = result.Pages.OriginalObjects.TryApplySelectedModification(
                new PlacementTransform(7f, 8f, 9f, 0f, 0f, 0f, 1f, 1f, 1f),
                report);

            Assert.False(applied);
            Assert.Equal(2, result.Session.Modifications.Count);
            Assert.Equal(before, result.Session.State);
            Assert.Empty(projector.AppliedModifications);
            Assert.Contains(
                report.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectEditConflict);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Delete_rejects_an_unresolved_imported_identity_conflict()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"first\",\"id\":\"first\"},{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"second\",\"id\":\"second\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            LevelEditSessionState stateBefore = result.Session.State;

            ApplyReport report = new();
            Assert.False(result.Pages.OriginalObjects.TryDeleteSelected(report));

            Assert.Equal(2, result.Session.Modifications.Count);
            Assert.Empty(result.Session.Deletions);
            Assert.Equal(stateBefore, result.Session.State);
            Assert.Empty(projector.AppliedDeletions);
            Assert.Contains(
                report.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectEditConflict);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Apply_modify_replaces_an_imported_deletion_for_the_same_identity()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport report = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 0f, 0f, 0f, 1f, 1f, 1f),
                    report),
                report.ToSummary("replace deletion"));

            FrameworkObjectData modification = Assert.Single(result.Session.Modifications);
            Assert.Equal(OriginalObjectEditor.EmptyModificationType, modification.Type);
            Assert.Empty(result.Session.Deletions);
            Assert.Single(projector.AppliedModifications);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Apply_modify_replaces_imported_deletion_by_reactivating_the_authored_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                active: false,
                selfActive: false,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport report = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    report),
                report.ToSummary("replace imported deletion"));

            OriginalObjectLiveState reactivated = Assert.Single(projector.RestoredStates);
            Assert.True(reactivated.IsActiveSelf);
            Assert.Equal(1f, reactivated.Transform.PositionX);
            Assert.Equal(9f, Assert.Single(projector.AppliedModifications).Position.X);
            Assert.Single(result.Session.Modifications);
            Assert.Empty(result.Session.Deletions);
            Assert.True(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Cancel_teardown_reactivates_authored_state_before_reapplying_a_modification()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                active: false,
                selfActive: false,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport modifyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    modifyReport),
                modifyReport.ToSummary("replace imported deletion"));

            ApplyReport teardownReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryPrepareTeardown(teardownReport),
                teardownReport.ToSummary("prepare teardown"));
            Assert.False(projector.RestoredStates[projector.RestoredStates.Count - 1].IsActiveSelf);
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);

            ApplyReport cancelReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryCancelTeardown(cancelReport),
                cancelReport.ToSummary("cancel teardown"));

            Assert.True(projector.RestoredStates[projector.RestoredStates.Count - 1].IsActiveSelf);
            Assert.Equal(2, projector.AppliedModifications.Count);
            Assert.Equal(9f, projector.AppliedModifications[1].Position.X);
            Assert.Single(result.Session.Modifications);
            Assert.Empty(result.Session.Deletions);
            Assert.True(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_cancel_teardown_restores_the_inactive_imported_preview()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                active: false,
                selfActive: false,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport modifyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    modifyReport),
                modifyReport.ToSummary("replace imported deletion"));
            ApplyReport teardownReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryPrepareTeardown(teardownReport),
                teardownReport.ToSummary("prepare teardown"));
            projector.FailModification = true;

            ApplyReport cancelReport = new();
            Assert.False(result.Pages.OriginalObjects.TryCancelTeardown(cancelReport));

            Assert.Single(result.Session.Modifications);
            Assert.Empty(result.Session.Deletions);
            Assert.False(projector.RestoredStates[projector.RestoredStates.Count - 1].IsActiveSelf);
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
            Assert.DoesNotContain(
                cancelReport.Entries,
                entry => entry.Code == ReportCodes.HistoryRollbackFailed);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_cancel_teardown_rolls_back_previews_reapplied_before_the_failure()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/FIRST",
                "first",
                true,
                new PlacementTransform(11f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "first");
            resolver.Add(
                "logic",
                "LOGIC/SECOND",
                "second",
                true,
                new PlacementTransform(22f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "second");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [
                        Entry("first", "decoration", "DECORATION/FIRST", true, 11f, 2f, 3f),
                        Entry("second", "logic", "LOGIC/SECOND", true, 22f, 2f, 3f)
                    ],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            ApplyOriginalModification(
                result,
                "decoration:DECORATION/FIRST",
                10f);
            ApplyOriginalModification(
                result,
                "logic:LOGIC/SECOND",
                20f);
            ApplyReport teardownReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryPrepareTeardown(teardownReport),
                teardownReport.ToSummary("prepare teardown"));
            projector.FailModificationPath = "LOGIC/SECOND";

            ApplyReport cancelReport = new();
            Assert.False(result.Pages.OriginalObjects.TryCancelTeardown(cancelReport));

            Assert.Equal(2, result.Session.Modifications.Count);
            Assert.Equal(4, projector.RestoredStates.Count);
            Assert.Equal(22f, projector.RestoredStates[2].Transform.PositionX);
            Assert.Equal(11f, projector.RestoredStates[3].Transform.PositionX);
            Assert.True(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
            Assert.Contains(
                cancelReport.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectModifierFailed);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Delete_replaces_an_imported_modification_for_the_same_identity()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"custom-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":3,\"y\":4,\"z\":5},\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"\",\"properties\":[]}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 3f, 4f, 5f));
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete imported modification"));
            Assert.Empty(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            Assert.False(projector.LastAppliedState.IsActiveSelf);

            ApplyReport undoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoReport),
                undoReport.ToSummary("undo imported deletion"));
            FrameworkObjectData restored = Assert.Single(result.Session.Modifications);
            Assert.Equal("imported-id", restored.Id);
            Assert.Empty(result.Session.Deletions);
            Assert.True(projector.LastAppliedState.IsActiveSelf);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Remove_restores_an_imported_deletion_instead_of_deleting_the_imported_edit()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                active: false,
                selfActive: false,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport applyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    applyReport),
                applyReport.ToSummary("modify"));
            Assert.Single(result.Session.Modifications);
            Assert.Empty(result.Session.Deletions);
            Assert.True(Assert.Single(projector.RestoredStates).IsActiveSelf);

            ApplyReport removeReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRemoveSelectedEdit(removeReport),
                removeReport.ToSummary("remove"));

            Assert.Empty(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            Assert.Equal(2, projector.RestoredStates.Count);
            OriginalObjectLiveState restored = projector.RestoredStates[1];
            Assert.False(restored.IsActiveSelf);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Preview_survives_editor_close_and_reopen_and_reset_restores_the_import_snapshot()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport applyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 0f, 0f, 0f, 1f, 1f, 1f),
                    applyReport),
                applyReport.ToSummary("modify"));

            EditorRuntimeState runtime = CreateRuntimeState();
            EditorModeGuard guard = new(
                () => runtime,
                value => runtime = value);
            Assert.True(
                guard.TryEnter(true, true, CreateRuntimeState(), out string error),
                error);
            Assert.True(guard.TryExit(out error), error);
            Assert.True(
                guard.TryEnter(true, true, CreateRuntimeState(), out error),
                error);

            ApplyReport pageReport = new();
            Assert.True(result.Pages.TrySwitchPage(pageReport));
            Assert.True(result.Pages.TrySwitchPage(pageReport));
            Assert.Single(result.Session.Modifications);
            Assert.NotNull(result.Pages.OriginalObjects.SelectedTransform);
            Assert.Empty(projector.RestoredStates);
            Assert.True(guard.TryExit(out error), error);

            ApplyReport resetReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryReset(resetReport),
                resetReport.ToSummary("reset"));

            Assert.Empty(result.Session.Modifications);
            Assert.Equal(LevelEditSessionState.Clean, result.Session.State);
            Assert.False(result.Pages.OriginalObjects.History.CanUndo);
            Assert.Single(projector.RestoredStates);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Original_and_addition_histories_are_routed_independently()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport originalReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 0f, 0f, 0f, 1f, 1f, 1f),
                    originalReport),
                originalReport.ToSummary("modify original"));

            ApplyReport additionReport = new();
            PlacementRecord placement;
            Assert.True(
                result.Manager.TryPlace(
                    BaseCreatorCatalog.BellIronType,
                    PlacementTransform.Default,
                    [],
                    additionReport,
                    out placement),
                additionReport.ToSummary("place"));
            Assert.True(
                result.Manager.TryMove(
                    placement.Id,
                    new PlacementTransform(12f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f),
                    additionReport),
                additionReport.ToSummary("move"));

            ApplyReport pageReport = new();
            Assert.True(result.Pages.TrySwitchPage(pageReport));
            ApplyReport undoAddition = new();
            Assert.True(result.Pages.TryUndo(undoAddition), undoAddition.ToSummary("undo addition"));
            Assert.True(result.Session.TryGetPlacement(placement.Id, out PlacementRecord restoredPlacement));
            Assert.Equal(PlacementTransform.Default, restoredPlacement.Transform);
            Assert.Single(result.Session.Modifications);

            Assert.True(result.Pages.TrySwitchPage(pageReport));
            ApplyReport undoOriginal = new();
            Assert.True(result.Pages.TryUndo(undoOriginal), undoOriginal.ToSummary("undo original"));
            Assert.Empty(result.Session.Modifications);
            Assert.Equal(LevelEditSessionState.Dirty, result.Session.State);

            Assert.True(result.Pages.TrySwitchPage(pageReport));
            ApplyReport redoAddition = new();
            Assert.True(result.Pages.TryRedo(redoAddition), redoAddition.ToSummary("redo addition"));
            Assert.True(result.Session.TryGetPlacement(placement.Id, out restoredPlacement));
            Assert.Equal(12f, restoredPlacement.Transform.PositionX);

            Assert.True(result.Pages.TrySwitchPage(pageReport));
            ApplyReport redoOriginal = new();
            Assert.True(result.Pages.TryRedo(redoOriginal), redoOriginal.ToSummary("redo original"));
            Assert.Equal(7f, Assert.Single(result.Session.Modifications).Position.X);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Reset_from_original_objects_page_restores_both_domains_and_clears_both_histories()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport originalReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 0f, 0f, 0f, 1f, 1f, 1f),
                    originalReport),
                originalReport.ToSummary("modify original"));
            ApplyReport additionReport = new();
            PlacementRecord placement;
            Assert.True(
                result.Manager.TryPlace(
                    BaseCreatorCatalog.BellIronType,
                    PlacementTransform.Default,
                    [],
                    additionReport,
                    out placement),
                additionReport.ToSummary("place"));
            Assert.True(result.Pages.OriginalObjects.History.CanUndo);
            Assert.True(result.Manager.History.CanUndo);

            ApplyReport resetReport = new();
            Assert.True(result.Pages.TryReset(resetReport), resetReport.ToSummary("reset"));

            Assert.Empty(result.Session.Modifications);
            Assert.Empty(result.Session.Deletions);
            Assert.Empty(result.Session.Placements);
            Assert.False(result.Pages.OriginalObjects.History.CanUndo);
            Assert.False(result.Pages.OriginalObjects.History.CanRedo);
            Assert.False(result.Manager.History.CanUndo);
            Assert.False(result.Manager.History.CanRedo);
            Assert.Equal(LevelEditSessionState.Clean, result.Session.State);
            Assert.Single(projector.RestoredStates);
            Assert.Equal(1f, projector.RestoredStates[0].Transform.PositionX);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_original_reset_restores_previews_already_restored_and_keeps_history()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("first", "decoration", "DECORATION/FIRST", true, 1f, 2f, 3f),
                Entry("second", "logic", "LOGIC/SECOND", true, 4f, 5f, 6f));
            ApplyOriginalModification(
                result,
                "decoration:DECORATION/FIRST",
                10f);
            ApplyOriginalModification(
                result,
                "logic:LOGIC/SECOND",
                20f);
            projector.FailRestoreOnAttempt = 2;

            ApplyReport report = new();
            Assert.False(result.Pages.OriginalObjects.TryReset(report));

            Assert.Equal(2, result.Session.Modifications.Count);
            Assert.True(result.Pages.OriginalObjects.History.CanUndo);
            Assert.Contains(
                projector.RestoreAttempts,
                attempt => attempt.Succeeded
                    && (attempt.State.Transform.PositionX == 10f
                        || attempt.State.Transform.PositionX == 20f));
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Reset_rollback_restores_the_projector_reported_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new()
            {
                AppliedStateFactory = _ => new OriginalObjectLiveState(
                    "actual",
                    false,
                    new PlacementTransform(55f, 56f, 57f, 0f, 0f, 0f, 1f, 1f, 1f),
                    "Renamed")
            };
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            ApplyOriginalModification(
                result,
                "decoration:DECORATION/HERO",
                10f);
            projector.FailRestoreOnAttempt = 1;

            ApplyReport report = new();
            Assert.False(result.Pages.OriginalObjects.TryReset(report));

            FakeOriginalObjectProjector.RestoreAttempt rollback = Assert.Single(
                projector.RestoreAttempts,
                attempt => attempt.Succeeded);
            Assert.Equal("Renamed", rollback.State.Name);
            Assert.False(rollback.State.IsActiveSelf);
            Assert.Equal(55f, rollback.State.Transform.PositionX);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_additions_reset_rolls_back_the_original_object_reset()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeRuntimeFactory factory = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                factory,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            ApplyOriginalModification(
                result,
                "decoration:DECORATION/HERO",
                10f);
            factory.FailDestroy = true;
            ApplyReport placeReport = new();
            Assert.True(
                result.Manager.TryPlace(
                    BaseCreatorCatalog.BellIronType,
                    PlacementTransform.Default,
                    [],
                    placeReport,
                    out _),
                placeReport.ToSummary("place"));

            ApplyReport resetReport = new();
            Assert.False(result.Pages.TryReset(resetReport));

            FrameworkObjectData modification =
                Assert.Single(result.Session.Modifications);
            Assert.Equal(10f, modification.Position.X);
            Assert.Single(result.Session.Placements);
            Assert.True(result.Pages.OriginalObjects.History.CanUndo);
            Assert.True(result.Manager.History.CanUndo);
            Assert.Equal(
                10f,
                projector.RestoredStates[
                    projector.RestoredStates.Count - 1].Transform.PositionX);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_original_teardown_restores_previews_already_restored_and_keeps_history()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("first", "decoration", "DECORATION/FIRST", true, 1f, 2f, 3f),
                Entry("second", "logic", "LOGIC/SECOND", true, 4f, 5f, 6f));
            ApplyOriginalModification(
                result,
                "decoration:DECORATION/FIRST",
                10f);
            ApplyOriginalModification(
                result,
                "logic:LOGIC/SECOND",
                20f);
            projector.FailRestoreOnAttempt = 2;

            ApplyReport report = new();
            Assert.False(result.Pages.OriginalObjects.TryTeardown(report));

            Assert.Equal(2, result.Session.Modifications.Count);
            Assert.True(result.Pages.OriginalObjects.History.CanUndo);
            Assert.Contains(
                projector.RestoreAttempts,
                attempt => attempt.Succeeded
                    && (attempt.State.Transform.PositionX == 10f
                        || attempt.State.Transform.PositionX == 20f));
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Undo_restores_local_active_state_when_the_object_is_inactive_in_hierarchy()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                active: false,
                selfActive: true,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                string.Empty);
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport applyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    applyReport),
                applyReport.ToSummary("modify"));

            ApplyReport undoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoReport),
                undoReport.ToSummary("undo"));

            OriginalObjectLiveState restored = Assert.Single(projector.RestoredStates);
            Assert.True(restored.IsActiveSelf);
            Assert.Equal(string.Empty, restored.Name);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Manager_clear_failure_keeps_original_history_and_reapplies_the_preview()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                true,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            FakeRuntimeFactory factory = new();
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                factory,
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            ApplyOriginalModification(
                result,
                "decoration:DECORATION/HERO",
                9f);
            factory.FailDestroy = true;
            ApplyReport placeReport = new();
            Assert.True(
                result.Manager.TryPlace(
                    BaseCreatorCatalog.BellIronType,
                    PlacementTransform.Default,
                    [],
                    placeReport,
                    out _),
                placeReport.ToSummary("place"));

            ApplyReport clearReport = new();
            Assert.False(reconciler.TryClear(clearReport));

            Assert.Same(result.Session, reconciler.CurrentSession);
            Assert.Same(result.Manager, reconciler.Manager);
            Assert.True(result.Pages.OriginalObjects.History.CanUndo);
            Assert.Single(projector.RestoredStates);
            Assert.Equal(2, projector.AppliedModifications.Count);
            Assert.Equal(9f, projector.AppliedModifications[1].Position.X);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Teardown_failure_keeps_the_session_and_original_history_available()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                true,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport applyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    applyReport),
                applyReport.ToSummary("modify"));
            projector.FailRestore = true;

            ApplyReport clearReport = new();
            Assert.False(reconciler.TryClear(clearReport));

            Assert.Same(result.Session, reconciler.CurrentSession);
            Assert.Same(result.Manager, reconciler.Manager);
            Assert.True(result.Pages.OriginalObjects.History.CanUndo);
            Assert.Contains(
                clearReport.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectLiveRestoreFailed);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Canonical_export_and_reimport_preserve_modification_and_all_arrays()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"unknown\",\"id\":\"preserved\"}],\"modifications\":[],\"deletions\":[{\"scene\":\"logic\",\"path\":\"LOGIC/OTHER\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f),
                Entry("other", "logic", "LOGIC/OTHER", true, 4f, 5f, 6f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport applyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(7f, 8f, 9f, 10f, 11f, 12f, 2f, 3f, 4f),
                    applyReport),
                applyReport.ToSummary("modify"));
            Assert.Equal(source, File.ReadAllText(path));

            LevelEditSessionExportResult exported = result.Session.Export(path);

            Assert.True(exported.Succeeded, exported.StatusMessage);
            JObject json = JObject.Parse(File.ReadAllText(path));
            Assert.Single((JArray)json["additions"]);
            Assert.Single((JArray)json["modifications"]);
            Assert.Single((JArray)json["deletions"]);
            Assert.Equal("preserved", (string)json["additions"][0]["id"]);
            Assert.Equal("editor-mode-empty-1", (string)json["modifications"][0]["id"]);
            Assert.Equal(7f, (float)json["modifications"][0]["position"]["x"]);

            FrameworkFileResult reimportedFile = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            FakeOriginalObjectProjector reimportedProjector = new();
            RoomOverlayReconcileResult reimported = Reconcile(
                path,
                File.ReadAllText(path),
                reimportedProjector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f),
                Entry("other", "logic", "LOGIC/OTHER", true, 4f, 5f, 6f));

            FrameworkObjectData modification = Assert.Single(reimported.Session.Modifications);
            Assert.Equal("editor-mode-empty-1", modification.Id);
            Assert.Equal(string.Empty, modification.Condition);
            Assert.Equal(7f, modification.Position.X);
            Assert.Single(reimported.Session.Deletions);
            Assert.Single(reimported.Session.ImportedDocument.Additions);
            Assert.NotNull(reimportedFile.Document);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Clearing_the_room_overlay_restores_live_original_objects()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                true,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                new OriginalObjectBaseline(
                    "D03Z02S06",
                    [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                    new ApplyReport()),
                resolver);
            Assert.True(result.Succeeded, result.StatusMessage);
            SelectOriginal(result, "decoration:DECORATION/HERO");
            ApplyReport applyReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    applyReport),
                applyReport.ToSummary("modify"));

            ApplyReport clearReport = new();
            Assert.True(reconciler.TryClear(clearReport), clearReport.ToSummary("clear"));

            Assert.Single(projector.RestoredStates);
            Assert.Equal(1f, projector.RestoredStates[0].Transform.PositionX);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Replacing_the_room_overlay_restores_the_previous_preview_before_import()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            FakeOriginalObjectPathResolver resolver = new();
            resolver.Add(
                "decoration",
                "DECORATION/HERO",
                "original",
                true,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                "original");
            RoomOverlayReconciler reconciler = new(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new FakeRuntimeFactory(),
                projector);
            OriginalObjectBaseline baseline = new(
                "D03Z02S06",
                [Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f)],
                new ApplyReport());
            RoomOverlayReconcileResult first = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                baseline,
                resolver);
            Assert.True(first.Succeeded, first.StatusMessage);
            SelectOriginal(first, "decoration:DECORATION/HERO");
            ApplyReport applyReport = new();
            Assert.True(
                first.Pages.OriginalObjects.TryApplySelectedModification(
                    new PlacementTransform(9f, 8f, 7f, 0f, 0f, 0f, 1f, 1f, 1f),
                    applyReport),
                applyReport.ToSummary("modify"));

            RoomOverlayReconcileResult replacement = reconciler.Apply(
                CreateRoom(),
                FileResult(path, source),
                baseline,
                resolver);

            Assert.True(replacement.Succeeded, replacement.StatusMessage);
            Assert.Single(projector.RestoredStates);
            Assert.Empty(replacement.Session.Modifications);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Original_object_properties_expose_raw_text_and_apply_the_normalized_list()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"custom-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"rotation\":{\"x\":4,\"y\":5,\"z\":6},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"\",\"properties\":[\"old=1\"]}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            OriginalObjectPage page = result.Pages.OriginalObjects;

            Assert.True(page.CanEditSelectedProperties);
            Assert.Equal("old=1", page.SelectedPropertiesText.Text);
            ApplyReport report = new();

            bool applied = page.TryApplySelectedProperties(
                "\r\n  first = 1 \rduplicate\t\nduplicate\n\nMiXeD\r\n",
                report);

            Assert.True(applied, report.ToSummary("apply properties"));
            string[] expected =
            [
                "first = 1",
                "duplicate",
                "duplicate",
                "MiXeD"
            ];
            FrameworkObjectData modification = Assert.Single(result.Session.Modifications);
            Assert.Equal(expected, modification.Properties);
            Assert.Equal(expected, Assert.Single(projector.AppliedModifications).Properties);
            Assert.Equal(1, report.AppliedCount);
            Assert.Equal(1, page.History.UndoCount);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Original_object_properties_clear_on_blank_and_no_op_normalized_input_adds_no_history()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"custom-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"rotation\":{\"x\":4,\"y\":5,\"z\":6},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"\",\"properties\":[\"a=1\",\"b=2\"]}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            OriginalObjectPage page = result.Pages.OriginalObjects;
            ApplyReport clearReport = new();

            Assert.True(
                page.TryApplySelectedProperties("\r\n \t\r\n", clearReport),
                clearReport.ToSummary("clear properties"));

            Assert.Empty(Assert.Single(result.Session.Modifications).Properties);
            Assert.Empty(Assert.Single(projector.AppliedModifications).Properties);
            Assert.Equal(1, page.History.UndoCount);
            result.Session.SetState(LevelEditSessionState.Clean, new ApplyReport());
            int appliedCount = projector.AppliedModifications.Count;
            ApplyReport noOpReport = new();

            Assert.True(
                page.TryApplySelectedProperties(" \r\n\t", noOpReport),
                noOpReport.ToSummary("no-op properties"));

            Assert.Equal(LevelEditSessionState.Clean, result.Session.State);
            Assert.Equal(1, page.History.UndoCount);
            Assert.Equal(appliedCount, projector.AppliedModifications.Count);
            Assert.Equal(1, noOpReport.AppliedCount);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Original_object_properties_with_committed_line_breaks_are_read_only_and_refuse_replacement()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"custom-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"rotation\":{\"x\":4,\"y\":5,\"z\":6},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"\",\"properties\":[\"note=first\\r\\nsecond\",\"safe=1\"]}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            OriginalObjectPage page = result.Pages.OriginalObjects;

            Assert.False(page.CanEditSelectedProperties);
            Assert.True(page.SelectedPropertiesText.ContainsLineBreak);
            ApplyReport report = new();

            Assert.False(page.TryApplySelectedProperties("replacement=2", report));

            Assert.Equal(
                new[] { "note=first\r\nsecond", "safe=1" },
                Assert.Single(result.Session.Modifications).Properties);
            Assert.Empty(projector.AppliedModifications);
            Assert.False(page.History.CanUndo);
            Assert.Contains(
                report.Entries,
                entry => entry.Code == ReportCodes.PropertyInvalid
                    && entry.Severity == ApplySeverity.Warning
                    && entry.Location.EndsWith(".properties[0]", StringComparison.Ordinal));
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Unknown_original_object_type_properties_remain_editable_and_exportable_without_live_application()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"unknown-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"rotation\":{\"x\":4,\"y\":5,\"z\":6},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"\",\"properties\":[\"old=1\"]}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            projector.UnavailableModifierTypes.Add("unknown-modifier");
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            OriginalObjectPage page = result.Pages.OriginalObjects;
            ApplyReport report = new();

            Assert.True(page.CanEditSelectedProperties);
            Assert.True(
                page.TryApplySelectedProperties("alpha\nunknown = raw\nALPHA", report),
                report.ToSummary("unknown type properties"));

            string[] expected = ["alpha", "unknown = raw", "ALPHA"];
            Assert.Equal(expected, Assert.Single(result.Session.Modifications).Properties);
            Assert.Empty(projector.AppliedModifications);
            Assert.Equal(1, report.AppliedCount);
            Assert.Contains(
                report.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectModifierUnavailable
                    && entry.Severity == ApplySeverity.Warning);
            Assert.Equal(source, File.ReadAllText(path));

            ApplyReport undoReport = new();
            Assert.True(page.TryUndo(undoReport), undoReport.ToSummary("undo unknown properties"));
            Assert.Equal(new[] { "old=1" }, Assert.Single(result.Session.Modifications).Properties);
            Assert.Empty(projector.AppliedModifications);

            ApplyReport redoReport = new();
            Assert.True(page.TryRedo(redoReport), redoReport.ToSummary("redo unknown properties"));
            Assert.Equal(expected, Assert.Single(result.Session.Modifications).Properties);
            Assert.Empty(projector.AppliedModifications);
            Assert.Equal(source, File.ReadAllText(path));

            LevelEditSessionExportResult exported = result.Session.Export(path);

            Assert.True(exported.Succeeded, exported.StatusMessage);
            JObject root = JObject.Parse(File.ReadAllText(path));
            Assert.Equal(
                expected,
                root["modifications"][0]["properties"].Values<string>());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Unavailable_modifier_delete_undo_redo_restores_imported_live_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"unknown-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":3,\"y\":4,\"z\":5},\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"\",\"properties\":[]}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            projector.UnavailableModifierTypes.Add("unknown-modifier");
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 3f, 4f, 5f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            Assert.True(result.Pages.OriginalObjects.SelectedIsRuntimeActive);

            ApplyReport deleteReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryDeleteSelected(deleteReport),
                deleteReport.ToSummary("delete unavailable modification"));
            Assert.Single(result.Session.Deletions);
            Assert.Empty(result.Session.Modifications);
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);

            int appliedModificationCount = projector.AppliedModifications.Count;
            ApplyReport undoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryUndo(undoReport),
                undoReport.ToSummary("undo unavailable modification deletion"));

            OriginalObjectLiveState restored = Assert.Single(projector.RestoredStates);
            Assert.True(restored.IsActiveSelf);
            Assert.Equal(1f, restored.Transform.PositionX);
            Assert.Equal(appliedModificationCount, projector.AppliedModifications.Count);
            Assert.Single(result.Session.Modifications);
            Assert.Empty(result.Session.Deletions);
            Assert.True(result.Pages.OriginalObjects.SelectedIsRuntimeActive);

            ApplyReport redoReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryRedo(redoReport),
                redoReport.ToSummary("redo unavailable modification deletion"));
            Assert.Single(result.Session.Deletions);
            Assert.Empty(result.Session.Modifications);
            Assert.False(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Cancel_teardown_keeps_unavailable_modification_on_imported_live_state()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"unknown-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":3,\"y\":4,\"z\":5},\"rotation\":{\"x\":0,\"y\":0,\"z\":0},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"\",\"properties\":[]}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            projector.UnavailableModifierTypes.Add("unknown-modifier");
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 3f, 4f, 5f));
            SelectOriginal(result, "decoration:DECORATION/HERO");

            ApplyReport teardownReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryPrepareTeardown(teardownReport),
                teardownReport.ToSummary("prepare teardown with unavailable modification"));
            int restoreCount = projector.RestoredStates.Count;

            ApplyReport cancelReport = new();
            Assert.True(
                result.Pages.OriginalObjects.TryCancelTeardown(cancelReport),
                cancelReport.ToSummary("cancel teardown with unavailable modification"));

            Assert.Equal(restoreCount + 1, projector.RestoredStates.Count);
            Assert.True(projector.RestoredStates[projector.RestoredStates.Count - 1].IsActiveSelf);
            Assert.Equal(1f, projector.RestoredStates[projector.RestoredStates.Count - 1].Transform.PositionX);
            Assert.Empty(projector.AppliedModifications);
            Assert.Single(result.Session.Modifications);
            Assert.True(result.Pages.OriginalObjects.SelectedIsRuntimeActive);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_original_object_property_projection_preserves_committed_state_and_source()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"custom-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"rotation\":{\"x\":4,\"y\":5,\"z\":6},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"\",\"properties\":[\"old=1\"]}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new()
            {
                FailModification = true
            };
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            OriginalObjectPage page = result.Pages.OriginalObjects;
            LevelEditSessionState stateBefore = result.Session.State;
            ApplyReport report = new();

            Assert.False(page.TryApplySelectedProperties("replacement=2", report));

            Assert.Equal(
                new[] { "old=1" },
                Assert.Single(result.Session.Modifications).Properties);
            Assert.Equal(stateBefore, result.Session.State);
            Assert.Empty(projector.AppliedModifications);
            Assert.False(page.History.CanUndo);
            Assert.Contains(
                report.Entries,
                entry => entry.Code == ReportCodes.OriginalObjectModifierFailed);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Original_object_property_history_and_canonical_round_trip_preserve_the_ordered_list()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"decoration\",\"path\":\"DECORATION/HERO\",\"type\":\"custom-modifier\",\"id\":\"imported-id\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"rotation\":{\"x\":4,\"y\":5,\"z\":6},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"\",\"properties\":[\"old=1\"],\"futureField\":{\"nested\":true}}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FakeOriginalObjectProjector projector = new();
            RoomOverlayReconcileResult result = Reconcile(
                path,
                source,
                projector,
                Entry("original", "decoration", "DECORATION/HERO", true, 1f, 2f, 3f));
            SelectOriginal(result, "decoration:DECORATION/HERO");
            OriginalObjectPage page = result.Pages.OriginalObjects;
            ApplyReport applyReport = new();
            Assert.True(
                page.TryApplySelectedProperties("first\nsecond\nfirst", applyReport),
                applyReport.ToSummary("apply history"));
            string[] expected = ["first", "second", "first"];
            Assert.Equal(expected, Assert.Single(result.Session.Modifications).Properties);
            Assert.Equal(0, result.Pages.Additions.History.UndoCount);

            ApplyReport undoReport = new();
            Assert.True(page.TryUndo(undoReport), undoReport.ToSummary("undo properties"));
            Assert.Equal(new[] { "old=1" }, Assert.Single(result.Session.Modifications).Properties);
            Assert.Equal(
                new[] { "old=1" },
                projector.AppliedModifications[projector.AppliedModifications.Count - 1].Properties);

            ApplyReport redoReport = new();
            Assert.True(page.TryRedo(redoReport), redoReport.ToSummary("redo properties"));
            Assert.Equal(expected, Assert.Single(result.Session.Modifications).Properties);
            Assert.Equal(
                expected,
                projector.AppliedModifications[projector.AppliedModifications.Count - 1].Properties);
            Assert.Equal(source, File.ReadAllText(path));

            LevelEditSessionExportResult exported = result.Session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
            JObject exportedRoot = JObject.Parse(File.ReadAllText(path));
            Assert.Null(exportedRoot["modifications"][0]["futureField"]);
            FrameworkCodecResult sourceParse = LevelFrameworkAdapter.Parse(source);
            Assert.Contains(
                sourceParse.Diagnostics,
                diagnostic => diagnostic.Severity == FrameworkDiagnosticSeverity.Warning
                    && diagnostic.Location.EndsWith(".futureField", StringComparison.Ordinal)
                    && diagnostic.Message == "Unknown field ignored.");
            Assert.Contains(
                result.Report.Entries,
                entry => entry.Severity == ApplySeverity.Warning
                    && entry.Code == ReportCodes.JsonUnknownField
                    && entry.Location.EndsWith(".futureField", StringComparison.Ordinal));
            FrameworkFileResult reimported = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            LevelEditSessionImportResult imported = LevelEditSession.Import(
                CreateRoom(),
                reimported,
                new[] { BaseCreatorCatalog.BellIron });

            Assert.True(imported.Succeeded, imported.StatusMessage);
            Assert.Equal(expected, Assert.Single(imported.Session.Modifications).Properties);
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
        params OriginalObjectSnapshotEntry[] entries)
    {
        return Reconcile(
            path,
            source,
            projector,
            new FakeRuntimeFactory(),
            entries);
    }

    private static RoomOverlayReconcileResult Reconcile(
        string path,
        string source,
        FakeOriginalObjectProjector projector,
        FakeRuntimeFactory factory,
        params OriginalObjectSnapshotEntry[] entries)
    {
        FrameworkFileResult file = FileResult(path, source);
        OriginalObjectBaseline baseline = new("D03Z02S06", entries, new ApplyReport());
        FakeOriginalObjectPathResolver resolver = new();
        foreach (OriginalObjectSnapshotEntry entry in entries)
        {
            resolver.Add(
                entry.Scene,
                entry.Path,
                entry.ObjectKey,
                entry.IsActive,
                new PlacementTransform(1f, 2f, 3f, 4f, 5f, 6f, 1f, 1f, 1f),
                entry.ObjectKey);
        }

        RoomOverlayReconciler reconciler = new(
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory,
            projector);
        RoomOverlayReconcileResult result = reconciler.Apply(
            CreateRoom(),
            file,
            baseline,
            resolver);
        Assert.True(result.Succeeded, result.StatusMessage);
        return result;
    }

    private static FrameworkFileResult FileResult(string path, string source)
    {
        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(source);
        return new FrameworkFileResult(
            path,
            source,
            parsed.Document,
            parsed.Diagnostics);
    }

    private static void SelectOriginal(RoomOverlayReconcileResult result, string identity)
    {
        ApplyReport report = new();
        if (result.Pages.ActivePage != EditorPage.OriginalObjects)
            Assert.True(result.Pages.TrySwitchPage(report));
        Assert.True(result.Pages.TrySelectOriginal(identity, report, out _));
    }

    private static void ApplyOriginalModification(
        RoomOverlayReconcileResult result,
        string identity,
        float positionX)
    {
        SelectOriginal(result, identity);
        ApplyReport report = new();
        Assert.True(
            result.Pages.OriginalObjects.TryApplySelectedModification(
                new PlacementTransform(
                    positionX,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    1f,
                    1f,
                    1f),
                report),
            report.ToSummary("modify " + identity));
    }

    private static OriginalObjectSnapshotEntry Entry(
        string objectKey,
        string scene,
        string path,
        bool active,
        float x,
        float y,
        float z)
    {
        return new OriginalObjectSnapshotEntry(
            objectKey,
            scene,
            path,
            active,
            OriginalObjectProvenance.Baseline,
            true);
    }

    private static RoomIdentity CreateRoom()
    {
        RoomIdentity room;
        string error;
        Assert.True(
            RoomIdentity.TryCreate("D03Z02S06", "D03Z02S06_LOGIC", out room, out error),
            error);
        return room;
    }

    private static EditorRuntimeState CreateRuntimeState()
    {
        return new EditorRuntimeState(
            1f,
            false,
            false,
            true,
            true,
            true,
            true,
            false,
            true,
            true,
            false,
            true,
            0);
    }

    private static string MakeDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "editor-mode-original-edit-" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeOriginalObjectPathResolver : IOriginalObjectPathResolver
    {
        private readonly Dictionary<string, OriginalObjectProjection> _projections = new(StringComparer.Ordinal);

        internal void Add(
            string scene,
            string path,
            string objectKey,
            bool active,
            bool? selfActive,
            PlacementTransform transform,
            string name)
        {
            _projections[Key(scene, path)] = new OriginalObjectProjection(
                objectKey,
                active,
                transform,
                name,
                selfActive);
        }

        internal void Add(
            string scene,
            string path,
            string objectKey,
            bool active,
            PlacementTransform transform,
            string name)
        {
            Add(scene, path, objectKey, active, null, transform, name);
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
        internal List<OriginalObjectIdentity> AppliedDeletions { get; } = [];
        internal List<OriginalObjectLiveState> RestoredStates { get; } = [];
        internal List<RestoreAttempt> RestoreAttempts { get; } = [];
        internal HashSet<string> UnavailableModifierTypes { get; } = new(StringComparer.Ordinal);
        internal Func<FrameworkObjectData, OriginalObjectLiveState> AppliedStateFactory { get; set; }
        internal Func<OriginalObjectIdentity, OriginalObjectLiveState> DeletedStateFactory { get; set; }
        internal bool FailModification { get; set; }
        internal string FailModificationPath { get; set; }
        internal bool FailDeletion { get; set; }
        internal bool FailRestore { get; set; }
        internal int FailRestoreOnAttempt { get; set; }
        internal OriginalObjectLiveState LastAppliedState { get; private set; }
        private int _restoreAttemptCount;

        public bool IsModifierAvailable(string type)
        {
            return !UnavailableModifierTypes.Contains(type);
        }

        public bool TryApplyModification(
            string frameworkLevelKey,
            FrameworkObjectData modification,
            ApplyReport report,
            out OriginalObjectLiveState appliedState)
        {
            appliedState = null;
            if (FailModification
                || string.Equals(
                    modification.Path,
                    FailModificationPath,
                    StringComparison.Ordinal))
            {
                report.Error(
                    ReportCodes.OriginalObjectModifierFailed,
                    modification.Scene + ":" + modification.Path,
                    "Modifier threw during the test.");
                return false;
            }

            AppliedModifications.Add(modification);
            appliedState = AppliedStateFactory?.Invoke(modification)
                ?? new OriginalObjectLiveState(
                    string.Empty,
                    true,
                    PlacementTransform.FromFramework(modification),
                    string.Empty);
            LastAppliedState = appliedState;
            return true;
        }

        public bool TryApplyDeletion(
            string frameworkLevelKey,
            OriginalObjectIdentity identity,
            OriginalObjectLiveState authoredState,
            ApplyReport report,
            out OriginalObjectLiveState appliedState)
        {
            appliedState = null;
            if (FailDeletion)
            {
                report.Error(
                    ReportCodes.OriginalObjectApplyFailed,
                    identity.DisplayText,
                    "Deletion projection failed during the test.");
                return false;
            }

            AppliedDeletions.Add(identity);
            appliedState = DeletedStateFactory?.Invoke(identity)
                ?? new OriginalObjectLiveState(
                    authoredState.ObjectKey,
                    false,
                    authoredState.Transform,
                    authoredState.Name);
            LastAppliedState = appliedState;
            return true;
        }

        public bool TryRestore(
            string frameworkLevelKey,
            OriginalObjectIdentity identity,
            OriginalObjectLiveState state,
            ApplyReport report)
        {
            _restoreAttemptCount++;
            bool succeeded = !FailRestore
                && (_restoreAttemptCount != FailRestoreOnAttempt);
            RestoreAttempts.Add(new RestoreAttempt(identity, state, succeeded));
            if (!succeeded)
            {
                report.Error(
                    ReportCodes.OriginalObjectLiveRestoreFailed,
                    identity.DisplayText,
                    "The test projector rejected live-state restoration.");
                return false;
            }

            RestoredStates.Add(state);
            return true;
        }

        internal sealed class RestoreAttempt
        {
            internal RestoreAttempt(
                OriginalObjectIdentity identity,
                OriginalObjectLiveState state,
                bool succeeded)
            {
                Identity = identity;
                State = state;
                Succeeded = succeeded;
            }

            internal OriginalObjectIdentity Identity { get; }
            internal OriginalObjectLiveState State { get; }
            internal bool Succeeded { get; }
        }
    }

    private sealed class FakeRuntimeFactory : IRuntimePlacementFactory
    {
        internal bool FailDestroy { get; set; }

        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            return new FakeHandle(placement.Id)
            {
                FailDestroy = FailDestroy
            };
        }
    }

    private sealed class FakeHandle : IRuntimePlacementHandle
    {
        internal FakeHandle(string placementId)
        {
            PlacementId = placementId;
            IsAlive = true;
        }

        public string PlacementId { get; }
        public bool IsOriginal { get { return false; } }
        public bool IsAlive { get; private set; }
        internal bool FailDestroy { get; set; }

        public bool TryApply(PlacementRecord placement, out string error)
        {
            error = null;
            return IsAlive;
        }

        public bool TryDestroy(out string error)
        {
            if (FailDestroy)
            {
                error = "The test runtime rejected cleanup.";
                return false;
            }

            error = null;
            IsAlive = false;
            return true;
        }
    }
}