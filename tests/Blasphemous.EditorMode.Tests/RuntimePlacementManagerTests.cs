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
}
