using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RoomOverlayReconcilerTests
{
    [Fact]
    public void Repeated_room_apply_replaces_old_projections_without_duplicates()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, DocumentWithPlacement());
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            var factory = new FakeFactory();
            var reconciler = new RoomOverlayReconciler(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                factory);
            RoomIdentity room = CreateRoom("D03Z02S06", "D03Z02S06_LOGIC");

            RoomOverlayReconcileResult first = reconciler.Apply(room, file);
            RoomOverlayReconcileResult second = reconciler.Apply(room, file);

            Assert.True(first.Succeeded, first.StatusMessage);
            Assert.True(second.Succeeded, second.StatusMessage);
            Assert.Single(second.Manager.OwnedPlacementIds);
            Assert.Equal(2, factory.Created.Count);
            Assert.False(factory.Created[0].IsAlive);
            Assert.True(factory.Created[1].IsAlive);
            Assert.Equal(room, reconciler.CurrentRoom);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_import_preserves_current_session_and_projection()
    {
        string path = "D03Z02S06.json";
        string source = DocumentWithPlacement();
        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(source);
        Assert.True(parsed.IsValid);
        var file = new FrameworkFileResult(path, source, parsed.Document, parsed.Diagnostics);
        var factory = new FakeFactory();
        var reconciler = new RoomOverlayReconciler(
            ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
            factory);
        RoomIdentity room = CreateRoom("D03Z02S06", "D03Z02S06_LOGIC");

        RoomOverlayReconcileResult initial = reconciler.Apply(room, file);
        Assert.True(initial.Succeeded, initial.StatusMessage);
        var unreadable = new FrameworkFileResult(
            path,
            null,
            null,
            new[]
            {
                new FrameworkDiagnostic(
                    FrameworkDiagnosticSeverity.Error,
                    path,
                    "Level Export file could not be read.")
            });
        RoomOverlayReconcileResult failed = reconciler.Apply(room, unreadable);

        Assert.False(failed.Succeeded);
        Assert.Same(initial.Session, reconciler.CurrentSession);
        Assert.Same(initial.Manager, reconciler.Manager);
        Assert.Single(factory.Created);
        Assert.True(factory.Created[0].IsAlive);
    }

    [Fact]
    public void Round_trip_comparer_covers_scope_identity_transform_properties_and_ownership()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, DocumentWithPlacement());
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            RoomIdentity room = CreateRoom("D03Z02S06", "D03Z02S06_LOGIC");
            LevelEditSessionImportResult expected = LevelEditSession.Import(
                room,
                file,
                new[] { BaseCreatorCatalog.BellIron });
            LevelEditSessionImportResult actual = LevelEditSession.Import(
                room,
                file,
                new[] { BaseCreatorCatalog.BellIron });
            var report = new ApplyReport();

            Assert.True(RoomOverlayComparer.Matches(expected.Session, actual.Session, report), report.ToSummary("compare"));

            RoomIdentity otherRoom = CreateRoom("D04Z01S01", "D04Z01S01_LOGIC");
            string otherPath = Path.Combine(directory, "D04Z01S01.json");
            File.WriteAllText(otherPath, DocumentWithPlacement());
            FrameworkFileResult otherFile = LevelFrameworkAdapter.ImportFile(otherPath, "D04Z01S01");
            LevelEditSessionImportResult wrongScope = LevelEditSession.Import(
                otherRoom,
                otherFile,
                new[] { BaseCreatorCatalog.BellIron });
            Assert.False(RoomOverlayComparer.Matches(expected.Session, wrongScope.Session, report));
            Assert.Contains(report.Entries, x => x.Code == "ROUND_TRIP_SCOPE");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Clear_destroys_all_owned_projections_and_forgets_the_room()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, DocumentWithPlacement());
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            var factory = new FakeFactory();
            var reconciler = new RoomOverlayReconciler(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                factory);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file);
            var report = new ApplyReport();

            Assert.True(reconciler.TryClear(report));
            Assert.Null(reconciler.Manager);
            Assert.Null(reconciler.CurrentRoom);
            Assert.Equal(1, factory.Created[0].DestroyCount);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Reset_restores_imported_placements_and_their_runtime_projections()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, DocumentWithPlacement());
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            var factory = new FakeFactory();
            var reconciler = new RoomOverlayReconciler(
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                factory);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file);
            var report = new ApplyReport();

            Assert.True(result.Succeeded, result.StatusMessage);
            PlacementRecord placement;
            Assert.True(result.Manager.TryPlace(
                BaseCreatorCatalog.BellIronType,
                PlacementTransform.Default,
                null,
                report,
                out placement));
            Assert.True(result.Manager.TryReset(report));
            Assert.Single(result.Session.Placements);
            Assert.Single(result.Manager.OwnedPlacementIds);
            Assert.Equal(2, factory.Created.Count);
            Assert.False(factory.Created[1].IsAlive);
            Assert.True(factory.Created[0].IsAlive);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string DocumentWithPlacement()
    {
        return "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"authored\",\"position\":{\"x\":1,\"y\":2,\"z\":3},\"condition\":\"flag:opened\"}],\"modifications\":[],\"deletions\":[]}";
    }

    private static RoomIdentity CreateRoom(string levelKey, string scene)
    {
        RoomIdentity room;
        string error;
        Assert.True(RoomIdentity.TryCreate(levelKey, scene, out room, out error), error);
        return room;
    }

    private static string MakeDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "editor-mode-reconcile-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private sealed class FakeFactory : IRuntimePlacementFactory
    {
        internal readonly List<FakeHandle> Created = new List<FakeHandle>();

        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            var handle = new FakeHandle(placement.Id);
            Created.Add(handle);
            return handle;
        }
    }

    private sealed class FakeHandle : IRuntimePlacementHandle
    {
        internal FakeHandle(string id)
        {
            PlacementId = id;
            IsAlive = true;
        }

        public string PlacementId { get; }
        public bool IsOriginal { get { return false; } }
        public bool IsAlive { get; private set; }
        internal int DestroyCount { get; private set; }

        public bool TryApply(PlacementRecord placement, out string error)
        {
            error = null;
            return IsAlive;
        }

        public bool TryDestroy(out string error)
        {
            error = null;
            DestroyCount++;
            IsAlive = false;
            return true;
        }
    }
}
