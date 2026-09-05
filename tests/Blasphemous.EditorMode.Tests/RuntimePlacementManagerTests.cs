using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using System;
using System.Collections.Generic;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RuntimePlacementManagerTests
{
    [Fact]
    public void Place_creates_unique_owned_records_and_projection_handles()
    {
        LevelEditSession session = CreateSession();
        ContentCatalog catalog = ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron });
        var factory = new FakeFactory();
        var manager = new RuntimePlacementManager(session, catalog, factory);
        var report = new ApplyReport();
        PlacementRecord first;
        PlacementRecord second;

        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out first));
        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out second));
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(PlacementOwnership.ArchitectOwned, first.Ownership);
        Assert.Equal(2, manager.OwnedPlacementIds.Count);
        Assert.Equal(2, session.Placements.Count);
        Assert.Equal(2, factory.Created.Count);
    }

    [Fact]
    public void Select_move_and_remove_update_domain_and_runtime_once()
    {
        LevelEditSession session = CreateSession();
        var factory = new FakeFactory();
        var manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        var report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out placement));

        PlacementRecord selected;
        Assert.True(manager.TrySelect(placement.Id, report, out selected));
        PlacementTransform moved = new PlacementTransform(4f, 5f, 0f, 0f, 0f, 10f, 1f, 1f, 1f);
        Assert.True(manager.TryMove(placement.Id, moved, report));
        Assert.True(session.TryGetPlacement(placement.Id, out selected));
        Assert.Equal(moved, selected.Transform);
        Assert.Equal(1, factory.Created[0].ApplyCount);

        Assert.True(manager.TryRemove(placement.Id, report));
        Assert.Empty(manager.OwnedPlacementIds);
        Assert.Empty(session.Placements);
        Assert.Equal(1, factory.Created[0].DestroyCount);
        Assert.False(manager.TryRemove(placement.Id, report));
    }

    [Fact]
    public void Original_runtime_objects_are_rejected_by_selection_and_creation()
    {
        LevelEditSession session = CreateSession();
        var factory = new FakeFactory { ReturnOriginal = true };
        var manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        var report = new ApplyReport();
        PlacementRecord placement;

        Assert.False(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out placement));
        Assert.Empty(session.Placements);

        PlacementRecord selected;
        Assert.False(manager.TrySelectRuntimeObject(
            new FakeHandle("original", true),
            report,
            out selected));
        Assert.Contains(report.Entries, x => x.Code == "OWNERSHIP_REJECTED");
    }

    [Fact]
    public void Generated_ids_are_sanitized_and_skip_existing_ids()
    {
        Assert.Equal(
            "bell-iron-2",
            PlacementIdGenerator.Next("bell iron", new[] { "bell-iron-1" }));
        Assert.Equal(
            "placement-1",
            PlacementIdGenerator.Next("!!!", new string[0]));
    }

    [Fact]
    public void Transform_and_property_failures_leave_previous_values_intact()
    {
        var creator = new CreatorMetadata(
            "limited",
            "Limited",
            "Test",
            CreatorOrigin.Architect,
            "scene",
            "path",
            TransformCapabilities.Position,
            new[]
            {
                new CreatorPropertyDefinition("count", CreatorPropertyType.Integer),
                new CreatorPropertyDefinition("mode", CreatorPropertyType.Enum, new[] { "safe", "fast" })
            },
            true);
        LevelEditSession session = CreateSession();
        var factory = new FakeFactory();
        var manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { creator }),
            factory);
        var report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(creator.Type, PlacementTransform.Default, null, report, out placement));

        Assert.True(manager.TrySetProperty(placement.Id, "count", "42", report));
        Assert.False(manager.TrySetProperty(placement.Id, "count", "bad", report));
        Assert.False(manager.TrySetProperty(placement.Id, "missing", "1", report));
        Assert.False(manager.TryMove(
            placement.Id,
            new PlacementTransform(1f, 0f, 0f, 0f, 5f, 0f, 1f, 1f, 1f),
            report));

        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Contains("count=42", placement.Properties);
        Assert.DoesNotContain("count=bad", placement.Properties);
        Assert.Equal(1, factory.Created[0].ApplyCount);
        Assert.Contains(report.Entries, x => x.Code == "PROPERTY_INVALID");
        Assert.Contains(report.Entries, x => x.Code == "PROPERTY_UNSUPPORTED");
        Assert.Contains(report.Entries, x => x.Code == "TRANSFORM_UNSUPPORTED");
    }

    [Fact]
    public void Copy_pick_and_paste_preserve_data_but_remap_the_id()
    {
        LevelEditSession session = CreateSession();
        var factory = new FakeFactory();
        var manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        var report = new ApplyReport();
        PlacementRecord source;
        PlacementTransform transform = new PlacementTransform(2f, 3f, 4f, 0f, 0f, 20f, 1f, 1f, 1f);
        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, transform, null, "flag:opened", report, out source));

        PlacementRecord cursor;
        Assert.True(manager.TryPick(source.Id, report, out cursor));
        Assert.Equal(source.Id, cursor.Id);
        Assert.Equal(source.Transform, cursor.Transform);
        PlacementRecord pasted;
        Assert.True(manager.TryPaste(report, out pasted));
        Assert.NotEqual(source.Id, pasted.Id);
        Assert.Equal(source.Type, pasted.Type);
        Assert.Equal(source.Transform, pasted.Transform);
        Assert.Equal(source.Condition, pasted.Condition);
        Assert.Equal(2, session.Placements.Count);
    }

    [Fact]
    public void Lock_is_editor_only_and_blocks_mutating_operations()
    {
        LevelEditSession session = CreateSession();
        var factory = new FakeFactory();
        var manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        var report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out placement));
        Assert.True(manager.TrySetLocked(placement.Id, true, report));
        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.True(placement.IsLocked);
        Assert.False(manager.TryMove(placement.Id, PlacementTransform.Default, report));
        Assert.False(manager.TryDelete(placement.Id, report));
        Assert.True(manager.TrySetLocked(placement.Id, false, report));
        Assert.True(manager.TryDelete(placement.Id, report));
        Assert.Contains(report.Entries, x => x.Code == "PLACEMENT_LOCKED");
    }

    [Fact]
    public void Inactive_conditions_are_not_projected_into_runtime()
    {
        LevelEditSession session = CreateSession();
        var factory = new InactiveConditionFactory();
        var manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        var report = new ApplyReport();
        PlacementRecord placement;

        Assert.False(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            "flag:not-opened",
            report,
            out placement));
        Assert.Null(placement);
        Assert.Empty(session.Placements);
        Assert.Equal(0, factory.CreateCount);
        Assert.Contains(report.Entries, x => x.Code == "CONDITION_UNMET");
    }

    [Fact]
    public void Undo_and_redo_restore_session_runtime_and_dirty_state()
    {
        LevelEditSession session = CreateSession();
        var factory = new FakeFactory();
        var manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        var report = new ApplyReport();
        PlacementRecord placement;

        Assert.Equal(LevelEditSessionState.Clean, session.State);
        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out placement));
        Assert.Equal(LevelEditSessionState.Dirty, session.State);
        Assert.True(manager.History.CanUndo);
        Assert.True(manager.TryUndo(report));
        Assert.Empty(session.Placements);
        Assert.Empty(manager.OwnedPlacementIds);
        Assert.Equal(LevelEditSessionState.Clean, session.State);
        Assert.True(manager.History.CanRedo);
        Assert.True(manager.TryRedo(report));
        Assert.Single(session.Placements);
        Assert.Equal(LevelEditSessionState.Dirty, session.State);
        Assert.Single(manager.OwnedPlacementIds);
        Assert.Equal(2, factory.Created.Count);
    }

    [Fact]
    public void Reset_discards_edit_history_and_keeps_imported_source_in_memory_only()
    {
        LevelEditSession session = CreateSession();
        string originalSource = session.OriginalSourceText;
        var factory = new FakeFactory();
        var manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        var report = new ApplyReport();
        PlacementRecord placement;

        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out placement));
        Assert.True(manager.TryReset(report));
        Assert.Empty(session.Placements);
        Assert.Empty(manager.OwnedPlacementIds);
        Assert.Equal(originalSource, session.OriginalSourceText);
        Assert.False(manager.History.CanUndo);
        Assert.False(manager.History.CanRedo);
        Assert.Equal(LevelEditSessionState.Clean, session.State);
        Assert.Equal(1, factory.Created[0].DestroyCount);
        Assert.Contains(report.Entries, x => x.Code == "SESSION_RESET");
    }

    private static LevelEditSession CreateSession()
    {
        FrameworkFileResult file = CreateEmptyFile();
        RoomIdentity room;
        string error;
        Assert.True(RoomIdentity.TryCreate("D03Z02S06", "D03Z02S06_LOGIC", out room, out error), error);
        LevelEditSessionImportResult result = LevelEditSession.Import(
            room,
            file,
            new[] { BaseCreatorCatalog.BellIron });
        Assert.True(result.Succeeded, result.StatusMessage);
        return result.Session;
    }

    private static FrameworkFileResult CreateEmptyFile()
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "editor-mode-runtime-" + Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(directory, "D03Z02S06.json");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(path, "{\"additions\":[],\"modifications\":[],\"deletions\":[]}");
        FrameworkFileResult result = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
        System.IO.Directory.Delete(directory, true);
        return result;
    }

    private sealed class FakeFactory : IRuntimePlacementFactory
    {
        internal bool ReturnOriginal;
        internal readonly List<FakeHandle> Created = new List<FakeHandle>();

        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            var handle = new FakeHandle(placement.Id, ReturnOriginal);
            Created.Add(handle);
            return handle;
        }
    }

    private sealed class FakeHandle : IRuntimePlacementHandle
    {
        internal FakeHandle(string placementId, bool original)
        {
            PlacementId = placementId;
            IsOriginal = original;
            IsAlive = true;
        }

        public string PlacementId { get; }
        public bool IsOriginal { get; }
        public bool IsAlive { get; private set; }
        internal int ApplyCount { get; private set; }
        internal int DestroyCount { get; private set; }

        public bool TryApply(PlacementRecord placement, out string error)
        {
            error = null;
            if (!IsAlive)
            {
                error = "dead";
                return false;
            }

            ApplyCount++;
            return true;
        }

        public bool TryDestroy(out string error)
        {
            error = null;
            if (!IsAlive)
            {
                error = "dead";
                return false;
            }

            DestroyCount++;
            IsAlive = false;
            return true;
        }
    }

    private sealed class InactiveConditionFactory : IRuntimePlacementFactory, IRuntimePlacementConditionFilter
    {
        internal int CreateCount { get; private set; }

        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            CreateCount++;
            return new FakeHandle(placement.Id, false);
        }

        public bool ShouldProject(PlacementRecord placement, out string reason)
        {
            reason = "Condition is false in the test runtime.";
            return false;
        }
    }
}
