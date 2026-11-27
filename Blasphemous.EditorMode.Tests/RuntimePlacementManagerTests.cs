using Blasphemous.EditorMode.Components;
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

public sealed class RuntimePlacementManagerTests
{
    [Fact]
    public void PlaceCreatesUniqueEditableOverlayPlacementsAndProjectionHandles()
    {
        LevelEditSession session = CreateSession();
        ContentCatalog catalog = ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron });
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(session, catalog, factory);
        ApplyReport report = new ApplyReport();
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
    public void SelectMoveAndRemoveUpdateDomainAndRuntimeOnce()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
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
    public void InspectorFieldGroupsRoundTripThroughSelectedPlacement()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            report,
            out placement));
        string[] position = ["4.25", "-5.5", "6.75"];
        string[] rotation = ["7.125", "-8.25", "9.5"];
        string[] scale = ["1.5", "2.5", "3.5"];

        Assert.True(TransformInspectorApplyPolicy.TryApplySelected(
            manager,
            index => position[index],
            index => rotation[index],
            index => scale[index],
            report));

        PlacementTransform expected = new(
            4.25f,
            -5.5f,
            6.75f,
            7.125f,
            -8.25f,
            9.5f,
            1.5f,
            2.5f,
            3.5f);
        Assert.True(session.TryGetPlacement(placement.Id, out PlacementRecord stored));
        Assert.Equal(expected, manager.Selected.Transform);
        Assert.Equal(expected, stored.Transform);
    }

    [Fact]
    public void OriginalRuntimeObjectsAreRejectedBySelectionAndCreation()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory { ReturnOriginal = true };
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
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
    public void GeneratedIdsAreSanitizedAndSkipExistingIds()
    {
        Assert.Equal(
            "bell-iron-2",
            PlacementIdGenerator.Next("bell iron", new[] { "bell-iron-1" }));
        Assert.Equal(
            "placement-1",
            PlacementIdGenerator.Next("!!!", new string[0]));
    }

    [Fact]
    public void TransformAndPropertyFailuresLeavePreviousValuesIntact()
    {
        CreatorMetadata creator = new CreatorMetadata(
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
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { creator }),
            factory);
        ApplyReport report = new ApplyReport();
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
    public void RuntimeApplyFailuresRestorePreviousRuntimeAndSessionValues()
    {
        CreatorMetadata creator = new CreatorMetadata(
            "limited",
            "Limited",
            "Test",
            CreatorOrigin.Architect,
            "scene",
            "path",
            TransformCapabilities.Position,
            new[] { new CreatorPropertyDefinition("count", CreatorPropertyType.Integer) },
            true);
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { creator }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(creator.Type, PlacementTransform.Default, null, report, out placement));
        Assert.True(manager.TrySetProperty(placement.Id, "count", "42", report));

        FakeHandle handle = factory.Created[0];
        handle.FailNextApplyAfterMutation = true;
        Assert.False(manager.TrySetProperty(placement.Id, "count", "7", report));
        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Contains("count=42", placement.Properties);
        Assert.DoesNotContain("count=7", placement.Properties);
        Assert.Contains("count=42", handle.AppliedPlacement.Properties);
        Assert.DoesNotContain("count=7", handle.AppliedPlacement.Properties);

        PlacementTransform moved = new PlacementTransform(1f, 2f, 0f, 0f, 0f, 0f, 1f, 1f, 1f);
        handle.FailNextApplyAfterMutation = true;
        Assert.False(manager.TryMove(placement.Id, moved, report));
        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Equal(PlacementTransform.Default, placement.Transform);
        Assert.Equal(PlacementTransform.Default, handle.AppliedPlacement.Transform);
        Assert.Contains(report.Entries, x => x.Code == "RUNTIME_APPLY_FAILED");
        Assert.DoesNotContain(report.Entries, x => x.Code == "RUNTIME_ROLLBACK_FAILED");
    }

    [Fact]
    public void ApplyPropertiesAcceptsExactThreeLineAcceptanceInput()
    {
        CreatorMetadata creator = new CreatorMetadata(
            "typed",
            "Typed",
            "Test",
            CreatorOrigin.Architect,
            "scene",
            "path",
            TransformCapabilities.Position,
            new[] { new CreatorPropertyDefinition("count", CreatorPropertyType.Integer) },
            true);
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { creator }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            creator.Type,
            PlacementTransform.Default,
            null,
            report,
            out placement));
        report = new ApplyReport();

        const string text = "length = 3.0\ncolor = #ff00ff\nOneWayDown";

        Assert.True(manager.TryApplyProperties(placement.Id, text, report), report.ToSummary("properties"));

        string[] expected =
        [
            "length = 3.0",
            "color = #ff00ff",
            "OneWayDown"
        ];
        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Equal(expected, placement.Properties);
        Assert.Equal(expected, factory.Created[0].AppliedPlacement.Properties);
        Assert.Equal(LevelEditSessionState.Dirty, session.State);
        Assert.Equal(2, manager.History.UndoCount);
        Assert.Equal(1, report.AppliedCount);
    }

    [Fact]
    public void ApplyPropertiesSplitsTrimsAndPreservesRawDuplicateEntries()
    {
        CreatorMetadata creator = new CreatorMetadata(
            "typed",
            "Typed",
            "Test",
            CreatorOrigin.Architect,
            "scene",
            "path",
            TransformCapabilities.Position,
            new[] { new CreatorPropertyDefinition("count", CreatorPropertyType.Integer) },
            true);
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { creator }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            creator.Type,
            PlacementTransform.Default,
            null,
            report,
            out placement));

        const string text =
            " \t\r\ncount = bad\t \rMiXeD = Value\n\nUnknown Field\r\nMiXeD = Value\r\nNoEquals Value  \n";

        Assert.True(manager.TryApplyProperties(placement.Id, text, report), report.ToSummary("properties"));

        string[] expected =
        [
            "count = bad",
            "MiXeD = Value",
            "Unknown Field",
            "MiXeD = Value",
            "NoEquals Value"
        ];
        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Equal(expected, placement.Properties);
        Assert.Equal(expected, factory.Created[0].AppliedPlacement.Properties);
    }

    [Fact]
    public void ApplyPropertiesClearsAllEntriesForBlankInput()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            report,
            out placement));
        Assert.True(
            manager.TryApplyProperties(placement.Id, "x=1\ny=2", report),
            report.ToSummary("initial properties"));
        report = new ApplyReport();

        Assert.True(manager.TryApplyProperties(placement.Id, "\r\n \t\r\n", report), report.ToSummary("properties"));

        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Empty(placement.Properties);
        Assert.Empty(factory.Created[0].AppliedPlacement.Properties);
    }

    [Fact]
    public void ApplyPropertiesNoOpReportsSuccessWithoutRuntimeDirtyOrHistoryChange()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            report,
            out placement));
        Assert.True(
            manager.TryApplyProperties(placement.Id, "x=1\nmode=Ready", report),
            report.ToSummary("initial properties"));
        report = new ApplyReport();
        session.SetState(LevelEditSessionState.Clean, new ApplyReport());
        int undoCount = manager.History.UndoCount;
        int applyCount = factory.Created[0].ApplyCount;
        report = new ApplyReport();

        Assert.True(
            manager.TryApplyProperties(placement.Id, "\r\n  x=1  \nmode=Ready\r\n", report),
            report.ToSummary("properties"));

        Assert.Equal(LevelEditSessionState.Clean, session.State);
        Assert.Equal(undoCount, manager.History.UndoCount);
        Assert.Equal(applyCount, factory.Created[0].ApplyCount);
        Assert.Equal(1, report.AppliedCount);
    }

    [Fact]
    public void ApplyPropertiesRejectsLockedPlacementWithoutMutation()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            report,
            out placement));
        Assert.True(
            manager.TryApplyProperties(placement.Id, "x=1", report),
            report.ToSummary("initial properties"));
        report = new ApplyReport();
        Assert.True(manager.TrySetLocked(placement.Id, true, report));
        LevelEditSessionState state = session.State;
        int undoCount = manager.History.UndoCount;
        int applyCount = factory.Created[0].ApplyCount;
        report = new ApplyReport();

        Assert.False(manager.TryApplyProperties(placement.Id, "y=2", report));

        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Equal(new[] { "x=1" }, placement.Properties);
        Assert.Equal(new[] { "x=1" }, factory.Created[0].AppliedPlacement.Properties);
        Assert.Equal(state, session.State);
        Assert.Equal(undoCount, manager.History.UndoCount);
        Assert.Equal(applyCount, factory.Created[0].ApplyCount);
        Assert.Contains(report.Entries, entry => entry.Code == "PLACEMENT_LOCKED");
    }

    [Fact]
    public void ApplyPropertiesRuntimeRejectionRestoresPreviousProjectionAndHistory()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            report,
            out placement));
        Assert.True(
            manager.TryApplyProperties(placement.Id, "x=1", report),
            report.ToSummary("initial properties"));
        report = new ApplyReport();
        FakeHandle handle = factory.Created[0];
        LevelEditSessionState state = session.State;
        int undoCount = manager.History.UndoCount;
        handle.FailNextApplyAfterMutation = true;
        report = new ApplyReport();

        Assert.False(manager.TryApplyProperties(placement.Id, "y=2", report));

        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Equal(new[] { "x=1" }, placement.Properties);
        Assert.Equal(new[] { "x=1" }, handle.AppliedPlacement.Properties);
        Assert.Equal(state, session.State);
        Assert.Equal(undoCount, manager.History.UndoCount);
        Assert.Contains(report.Entries, entry => entry.Code == "RUNTIME_APPLY_FAILED");
        Assert.DoesNotContain(report.Entries, entry => entry.Code == "RUNTIME_ROLLBACK_FAILED");
    }

    [Fact]
    public void ApplyPropertiesRuntimeExceptionRestoresPreviousProjectionAndHistory()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            report,
            out placement));
        Assert.True(
            manager.TryApplyProperties(placement.Id, "x=1", report),
            report.ToSummary("initial properties"));
        report = new ApplyReport();
        FakeHandle handle = factory.Created[0];
        LevelEditSessionState state = session.State;
        int undoCount = manager.History.UndoCount;
        handle.ThrowNextApplyAfterMutation = true;

        Assert.False(manager.TryApplyProperties(placement.Id, "y=2", report));

        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Equal(new[] { "x=1" }, placement.Properties);
        Assert.Equal(new[] { "x=1" }, handle.AppliedPlacement.Properties);
        Assert.Equal(state, session.State);
        Assert.Equal(undoCount, manager.History.UndoCount);
        ApplyReportEntry failure = Assert.Single(
            report.Entries,
            entry => entry.Code == "RUNTIME_APPLY_FAILED");
        Assert.Equal(placement.Id, failure.Location);
        Assert.Contains("InvalidOperationException", failure.Message);
        Assert.DoesNotContain(report.Entries, entry => entry.Code == "RUNTIME_ROLLBACK_FAILED");
    }

    [Fact]
    public void ApplyPropertiesRollbackExceptionReportsRuntimeRollbackFailure()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            report,
            out placement));
        Assert.True(
            manager.TryApplyProperties(placement.Id, "x=1", report),
            report.ToSummary("initial properties"));
        report = new ApplyReport();
        FakeHandle handle = factory.Created[0];
        LevelEditSessionState state = session.State;
        int undoCount = manager.History.UndoCount;
        handle.ThrowOnApply = true;

        Assert.False(manager.TryApplyProperties(placement.Id, "y=2", report));

        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Equal(new[] { "x=1" }, placement.Properties);
        Assert.Equal(state, session.State);
        Assert.Equal(undoCount, manager.History.UndoCount);
        Assert.Contains(
            report.Entries,
            entry => entry.Code == "RUNTIME_APPLY_FAILED" && entry.Location == placement.Id);
        ApplyReportEntry rollback = Assert.Single(
            report.Entries,
            entry => entry.Code == "RUNTIME_ROLLBACK_FAILED");
        Assert.Equal(placement.Id, rollback.Location);
        Assert.Contains("InvalidOperationException", rollback.Message);
    }

    [Fact]
    public void ApplyPropertiesRejectsCommittedLineBreaksWithPerPropertyDiagnostics()
    {
        CreatorMetadata creator = new CreatorMetadata(
            "lined",
            "Lined",
            "Test",
            CreatorOrigin.Architect,
            "scene",
            "path",
            TransformCapabilities.Position,
            new[]
            {
                new CreatorPropertyDefinition("safe", CreatorPropertyType.String),
                new CreatorPropertyDefinition("note", CreatorPropertyType.String),
                new CreatorPropertyDefinition("middle", CreatorPropertyType.String)
            },
            true);
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { creator }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        string[] committed =
        [
            "safe=1",
            "note=first\r\nsecond",
            "middle=2",
            "note=third\nfourth"
        ];
        Assert.True(manager.TryPlace(
            creator.Type,
            PlacementTransform.Default,
            committed,
            report,
            out placement));
        LevelEditSessionState state = session.State;
        int undoCount = manager.History.UndoCount;
        int applyCount = factory.Created[0].ApplyCount;
        report = new ApplyReport();

        Assert.False(manager.TryApplyProperties(placement.Id, "replacement=1", report));

        Assert.True(session.TryGetPlacement(placement.Id, out placement));
        Assert.Equal(committed, placement.Properties);
        Assert.Equal(committed, factory.Created[0].AppliedPlacement.Properties);
        Assert.Equal(state, session.State);
        Assert.Equal(undoCount, manager.History.UndoCount);
        Assert.Equal(applyCount, factory.Created[0].ApplyCount);
        ApplyReportEntry first = Assert.Single(
            report.Entries,
            entry => entry.Location == "additions[" + placement.Id + "].properties[1]");
        Assert.Equal("PROPERTY_INVALID", first.Code);
        Assert.Contains("embedded CR or LF", first.Message);
        ApplyReportEntry second = Assert.Single(
            report.Entries,
            entry => entry.Location == "additions[" + placement.Id + "].properties[3]");
        Assert.Equal("PROPERTY_INVALID", second.Code);
    }

    [Fact]
    public void ApplyPropertiesUndoRedoAndExportPreserveCompleteOrderedList()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord placement;
        Assert.True(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            report,
            out placement));
        Assert.True(
            manager.TryApplyProperties(placement.Id, "before=1", report),
            report.ToSummary("initial properties"));
        report = new ApplyReport();

        const string appliedText = "length = 3.0\ncolor = #ff00ff\nOneWayDown\nOneWayDown";
        string[] applied =
        [
            "length = 3.0",
            "color = #ff00ff",
            "OneWayDown",
            "OneWayDown"
        ];
        int undoCountBeforeApply = manager.History.UndoCount;
        Assert.True(manager.TryApplyProperties(placement.Id, appliedText, report), report.ToSummary("properties"));
        Assert.Equal(undoCountBeforeApply + 1, manager.History.UndoCount);

        string directory = Path.Combine(
            Path.GetTempPath(),
            "editor-mode-properties-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, session.Room.ExportFileKey + ".json");
        try
        {
            Directory.CreateDirectory(directory);
            LevelEditSessionExportResult exported = session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
            Assert.Equal(applied, ExportedProperties(path, placement.Id));

            Assert.True(manager.TryUndo(report), report.ToSummary("undo"));
            Assert.True(session.TryGetPlacement(placement.Id, out placement));
            Assert.Equal(new[] { "before=1" }, placement.Properties);
            Assert.Equal(new[] { "before=1" }, factory.Created[0].AppliedPlacement.Properties);

            Assert.True(manager.TryRedo(report), report.ToSummary("redo"));
            Assert.True(session.TryGetPlacement(placement.Id, out placement));
            Assert.Equal(applied, placement.Properties);
            Assert.Equal(applied, factory.Created[0].AppliedPlacement.Properties);

            exported = session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
            Assert.Equal(applied, ExportedProperties(path, placement.Id));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void CopyPickAndPastePreserveDataButRemapTheId()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
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
    public void PastePreservesRawPropertiesAcceptedByApply()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord source;
        Assert.True(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            null,
            report,
            out source));
        Assert.True(
            manager.TryApplyProperties(source.Id, "OneWayDown\nlength = 3.0", report),
            report.ToSummary("raw properties"));

        PlacementRecord cursor;
        Assert.True(manager.TryPick(source.Id, report, out cursor));
        PlacementRecord pasted;
        Assert.True(manager.TryPaste(report, out pasted), report.ToSummary("paste"));

        string[] expected = ["OneWayDown", "length = 3.0"];
        Assert.Equal(expected, pasted.Properties);
        Assert.True(session.TryGetPlacement(pasted.Id, out PlacementRecord committed));
        Assert.Equal(expected, committed.Properties);
        Assert.Equal(2, session.Placements.Count);

        ApplyReport invalidReport = new ApplyReport();
        PlacementRecord rejected;
        Assert.False(manager.TryPlace(
            BaseCreatorCatalog.BellIronType,
            PlacementTransform.Default,
            new[] { "OneWayDown" },
            null,
            invalidReport,
            out rejected));
        Assert.Contains(invalidReport.Entries, entry => entry.Code == "PROPERTY_UNSUPPORTED");
    }

    [Fact]
    public void LockIsEditorOnlyAndBlocksMutatingOperations()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
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
    public void InactiveConditionsAreNotProjectedIntoRuntime()
    {
        LevelEditSession session = CreateSession();
        InactiveConditionFactory factory = new InactiveConditionFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
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
    public void UndoAndRedoRestoreSessionRuntimeAndDirtyState()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
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
    public void ResetDiscardsEditHistoryAndKeepsImportedSourceInMemoryOnly()
    {
        LevelEditSession session = CreateSession();
        string originalSource = session.OriginalSourceText;
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
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

    [Fact]
    public void ResetRestoresEarlierProjectionsWhenALaterDestroyFails()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord first;
        PlacementRecord second;

        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out first));
        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out second));
        factory.FailDestroyCall = 2;

        Assert.False(manager.TryReset(report));

        Assert.True(report.HasErrors);
        Assert.Contains(report.Entries, entry => entry.Code == "HISTORY_RESTORE_FAILED");
        Assert.Equal(LevelEditSessionState.Dirty, session.State);
        Assert.Equal(2, session.Placements.Count);
        Assert.Contains(session.Placements, placement => placement.Id == first.Id);
        Assert.Contains(session.Placements, placement => placement.Id == second.Id);
        Assert.Equal(2, manager.OwnedPlacementIds.Count);
        Assert.Contains(factory.Created, handle => handle.PlacementId == first.Id && handle.IsAlive);
        Assert.Contains(factory.Created, handle => handle.PlacementId == second.Id && handle.IsAlive);
        Assert.True(manager.History.CanUndo);
    }

    [Fact]
    public void ResetReportsThePlacementWhenProjectionRollbackFails()
    {
        LevelEditSession session = CreateSession();
        FakeFactory factory = new FakeFactory();
        RuntimePlacementManager manager = new RuntimePlacementManager(
            session,
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        ApplyReport report = new ApplyReport();
        PlacementRecord first;
        PlacementRecord second;

        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out first));
        Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out second));
        factory.FailDestroyCall = 2;
        factory.FailCreateCall = 3;

        Assert.False(manager.TryReset(report));

        FakeHandle destroyed = Assert.Single(factory.Created.Where(handle => !handle.IsAlive));
        Assert.Contains(
            report.Entries,
            entry => entry.Code == "HISTORY_ROLLBACK_FAILED" && entry.Location == destroyed.PlacementId);
        Assert.Equal(LevelEditSessionState.Dirty, session.State);
        Assert.Equal(2, session.Placements.Count);
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

    private static string[] ExportedProperties(string path, string placementId)
    {
        JObject document = JObject.Parse(File.ReadAllText(path));
        JObject addition = Assert.Single(
            document["additions"].Children<JObject>(),
            item => string.Equals((string)item["id"], placementId, StringComparison.Ordinal));
        return addition["properties"].ToObject<string[]>();
    }

    private sealed class FakeFactory : IRuntimePlacementFactory
    {
        internal bool ReturnOriginal;
        internal readonly List<FakeHandle> Created = new List<FakeHandle>();
        internal int FailCreateCall;
        internal int FailDestroyCall;
        private int _createCallCount;
        private int _destroyCallCount;

        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            _createCallCount++;
            if (_createCallCount == FailCreateCall)
            {
                return null;
            }

            FakeHandle handle = new FakeHandle(placement.Id, ReturnOriginal, placement, this);
            Created.Add(handle);
            return handle;
        }

        internal bool ShouldFailDestroy()
        {
            _destroyCallCount++;
            return _destroyCallCount == FailDestroyCall;
        }
    }

    private sealed class FakeHandle : IRuntimePlacementHandle
    {
        private readonly FakeFactory _factory;

        internal FakeHandle(string placementId, bool original, PlacementRecord appliedPlacement = null)
            : this(placementId, original, appliedPlacement, null)
        {
        }

        internal FakeHandle(
            string placementId,
            bool original,
            PlacementRecord appliedPlacement,
            FakeFactory factory)
        {
            PlacementId = placementId;
            IsOriginal = original;
            IsAlive = true;
            AppliedPlacement = appliedPlacement;
            _factory = factory;
        }

        public string PlacementId { get; }
        public bool IsOriginal { get; }
        public bool IsAlive { get; private set; }
        internal int ApplyCount { get; private set; }
        internal int DestroyCount { get; private set; }
        internal bool FailNextApplyAfterMutation;
        internal bool ThrowNextApplyAfterMutation;
        internal bool ThrowOnApply;
        internal PlacementRecord AppliedPlacement { get; private set; }

        public bool TryApply(PlacementRecord placement, out string error)
        {
            error = null;
            if (!IsAlive)
            {
                error = "dead";
                return false;
            }

            ApplyCount++;
            AppliedPlacement = placement;
            if (ThrowOnApply || ThrowNextApplyAfterMutation)
            {
                ThrowNextApplyAfterMutation = false;
                throw new InvalidOperationException("injected apply exception after partial mutation");
            }
            if (FailNextApplyAfterMutation)
            {
                FailNextApplyAfterMutation = false;
                error = "injected apply failure after partial mutation";
                return false;
            }

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
            if (_factory != null && _factory.ShouldFailDestroy())
            {
                error = "injected destroy failure";
                return false;
            }

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
