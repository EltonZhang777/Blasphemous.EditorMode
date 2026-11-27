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
            FakeFactory factory = new FakeFactory();
            RoomOverlayReconciler reconciler = new RoomOverlayReconciler(
                ContentCatalog.FromEntries(new[] { CreatorFixtures.BellIron }),
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
    public void Multiple_placements_share_one_adoption_lookup_and_adopt_compatible_markers_once()
    {
        string source = DocumentWithPlacements();
        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(source);
        Assert.True(parsed.IsValid);
        FrameworkFileResult file = new FrameworkFileResult(
            "D03Z02S06.json",
            source,
            parsed.Document,
            parsed.Diagnostics);
        FakeFrameworkMarker first = new FakeFrameworkMarker("room", "Framework", "first", "bell-iron");
        FakeFrameworkMarker second = new FakeFrameworkMarker("room", "Framework", "second", "bell-iron");
        FakeFrameworkMarker wrongRoom = new FakeFrameworkMarker("other-room", "Framework", "first", "bell-iron");
        FakeFrameworkMarker wrongSource = new FakeFrameworkMarker("room", "EditorMode", "second", "bell-iron");
        FakeFrameworkMarker wrongType = new FakeFrameworkMarker("room", "Framework", "first", "other");
        FakeAdoptingFactory factory = new FakeAdoptingFactory(
            new[] { first, second, wrongRoom, wrongSource, wrongType });
        RoomOverlayReconciler reconciler = new RoomOverlayReconciler(
            ContentCatalog.FromEntries(new[] { CreatorFixtures.BellIron }),
            factory);
        RoomOverlayReconcileResult result = reconciler.Apply(
            CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
            file);

        Assert.True(result.Succeeded, result.StatusMessage);
        Assert.Equal(1, factory.LookupBuildCount);
        Assert.Equal(1, factory.AdoptionPassCount);
        Assert.Equal(1, factory.AdoptionPassEndCount);
        Assert.Equal(2, factory.Adopted.Count);
        Assert.Equal(1, first.AdoptionCount);
        Assert.Equal(1, second.AdoptionCount);
        Assert.Equal(0, wrongRoom.AdoptionCount);
        Assert.Equal(0, wrongSource.AdoptionCount);
        Assert.Equal(0, wrongType.AdoptionCount);
    }

    [Fact]
    public void Failed_import_preserves_current_session_and_projection()
    {
        string path = "D03Z02S06.json";
        string source = DocumentWithPlacement();
        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(source);
        Assert.True(parsed.IsValid);
        FrameworkFileResult file = new FrameworkFileResult(path, source, parsed.Document, parsed.Diagnostics);
        FakeFactory factory = new FakeFactory();
        RoomOverlayReconciler reconciler = new RoomOverlayReconciler(
            ContentCatalog.FromEntries(new[] { CreatorFixtures.BellIron }),
            factory);
        RoomIdentity room = CreateRoom("D03Z02S06", "D03Z02S06_LOGIC");

        RoomOverlayReconcileResult initial = reconciler.Apply(room, file);
        Assert.True(initial.Succeeded, initial.StatusMessage);
        FrameworkFileResult unreadable = new FrameworkFileResult(
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
    public void Unavailable_registry_keeps_schema_less_additions_editable_and_properties_preserved()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, DocumentWithEditableAndPropertyAddition());
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            ContentCatalog catalog = ContentCatalog.FromInventory(
                RuntimeCreatorInventory.RegistryUnavailable("registry denied"));
            FakeFactory factory = new FakeFactory();
            RoomOverlayReconciler reconciler = new RoomOverlayReconciler(catalog, factory);
            RoomIdentity room = CreateRoom("D03Z02S06", "D03Z02S06_LOGIC");

            RoomOverlayReconcileResult result = reconciler.Apply(room, file);

            Assert.True(result.Succeeded, result.StatusMessage);
            Assert.Contains(
                result.Report.Entries,
                entry => entry.Code == ReportCodes.CreatorRegistryUnavailable);
            Assert.Contains(
                result.Report.Entries,
                entry => entry.Code == ReportCodes.PropertyUnsupported
                    && entry.Location == "property");
            PlacementRecord editable = Assert.Single(result.Session.Placements);
            Assert.Equal("editable", editable.Id);
            Assert.Single(result.Manager.OwnedPlacementIds);

            ApplyReport report = new ApplyReport();
            Assert.True(result.Manager.TrySelect(editable.Id, report, out _), report.ToSummary("select"));
            PlacementTransform moved = new(4f, 5f, 0f, 0f, 0f, 15f, 1f, 1f, 1f);
            Assert.True(result.Manager.TryMove(editable.Id, moved, report), report.ToSummary("move"));
            Assert.True(result.Manager.TryUndo(report), report.ToSummary("undo"));
            Assert.True(result.Manager.TryRedo(report), report.ToSummary("redo"));
            Assert.Equal(moved, result.Session.Placements[0].Transform);

            LevelEditSessionExportResult exported = result.Session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);

            FrameworkFileResult reloadedFile = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            RoomOverlayReconciler reloaded = new RoomOverlayReconciler(catalog, new FakeFactory());
            RoomOverlayReconcileResult reloadedResult = reloaded.Apply(room, reloadedFile);

            Assert.True(reloadedResult.Succeeded, reloadedResult.StatusMessage);
            PlacementRecord reloadedEditable = Assert.Single(reloadedResult.Session.Placements);
            Assert.Equal("editable", reloadedEditable.Id);
            Assert.Equal(moved, reloadedEditable.Transform);
            Assert.Contains(
                reloadedResult.Report.Entries,
                entry => entry.Code == ReportCodes.PropertyUnsupported);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Unavailable_registry_keeps_the_session_when_runtime_projection_fails()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, DocumentWithPlacement());
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            ContentCatalog catalog = ContentCatalog.FromInventory(
                RuntimeCreatorInventory.RegistryUnavailable("registry denied"));
            RoomOverlayReconciler reconciler = new RoomOverlayReconciler(catalog, new RejectingFactory());

            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file);

            Assert.True(result.Succeeded, result.StatusMessage);
            Assert.Single(result.Session.Placements);
            Assert.Empty(result.Manager.OwnedPlacementIds);
            Assert.Contains(
                result.Report.Entries,
                entry => entry.Code == ReportCodes.RuntimeCreateFailed);

            LevelEditSessionExportResult exported = result.Session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Unavailable_registry_keeps_the_existing_manager_when_same_room_reprojection_fails()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, DocumentWithPlacement());
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            ContentCatalog catalog = ContentCatalog.FromInventory(
                RuntimeCreatorInventory.RegistryUnavailable("registry denied"));
            SwitchableFactory factory = new SwitchableFactory();
            RoomOverlayReconciler reconciler = new RoomOverlayReconciler(catalog, factory);
            RoomIdentity room = CreateRoom("D03Z02S06", "D03Z02S06_LOGIC");

            RoomOverlayReconcileResult first = reconciler.Apply(room, file);
            Assert.True(first.Succeeded, first.StatusMessage);
            factory.Reject = true;

            RoomOverlayReconcileResult second = reconciler.Apply(room, file);

            Assert.True(second.Succeeded, second.StatusMessage);
            Assert.Same(first.Session, second.Session);
            Assert.Same(first.Manager, second.Manager);
            Assert.Single(second.Manager.OwnedPlacementIds);
            Assert.True(factory.Created[0].IsAlive);

            ApplyReport report = new ApplyReport();
            Assert.True(second.Manager.TrySelect("authored", report, out _), report.ToSummary("select"));
            PlacementTransform moved = new(7f, 8f, 0f, 0f, 0f, 30f, 1f, 1f, 1f);
            Assert.True(second.Manager.TryMove("authored", moved, report), report.ToSummary("move"));
            Assert.True(second.Manager.TryUndo(report), report.ToSummary("undo"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
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
                new[] { CreatorFixtures.BellIron });
            LevelEditSessionImportResult actual = LevelEditSession.Import(
                room,
                file,
                new[] { CreatorFixtures.BellIron });
            ApplyReport report = new ApplyReport();

            Assert.True(RoomOverlayComparer.Matches(expected.Session, actual.Session, report), report.ToSummary("compare"));

            RoomIdentity otherRoom = CreateRoom("D04Z01S01", "D04Z01S01_LOGIC");
            string otherPath = Path.Combine(directory, "D04Z01S01.json");
            File.WriteAllText(otherPath, DocumentWithPlacement());
            FrameworkFileResult otherFile = LevelFrameworkAdapter.ImportFile(otherPath, "D04Z01S01");
            LevelEditSessionImportResult wrongScope = LevelEditSession.Import(
                otherRoom,
                otherFile,
                new[] { CreatorFixtures.BellIron });
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
            FakeFactory factory = new FakeFactory();
            RoomOverlayReconciler reconciler = new RoomOverlayReconciler(
                ContentCatalog.FromEntries(new[] { CreatorFixtures.BellIron }),
                factory);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file);
            ApplyReport report = new ApplyReport();

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
            FakeFactory factory = new FakeFactory();
            RoomOverlayReconciler reconciler = new RoomOverlayReconciler(
                ContentCatalog.FromEntries(new[] { CreatorFixtures.BellIron }),
                factory);
            RoomOverlayReconcileResult result = reconciler.Apply(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file);
            ApplyReport report = new ApplyReport();

            Assert.True(result.Succeeded, result.StatusMessage);
            PlacementRecord placement;
            Assert.True(result.Manager.TryPlace(
                CreatorFixtures.BellIronType,
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

    private static string DocumentWithPlacements()
    {
        return "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"first\",\"position\":{\"x\":1,\"y\":2,\"z\":3}},{\"type\":\"bell-iron\",\"id\":\"second\",\"position\":{\"x\":4,\"y\":5,\"z\":6}}],\"modifications\":[],\"deletions\":[]}";
    }

    private static string DocumentWithEditableAndPropertyAddition()
    {
        return "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"editable\",\"position\":{\"x\":1,\"y\":2,\"z\":3}},{\"type\":\"bell-iron\",\"id\":\"property\",\"position\":{\"x\":4,\"y\":5,\"z\":6},\"properties\":[\"length=3.0\"]}],\"modifications\":[],\"deletions\":[]}";
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
            FakeHandle handle = new FakeHandle(placement.Id);
            Created.Add(handle);
            return handle;
        }
    }

    private sealed class RejectingFactory : IRuntimePlacementFactory
    {
        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            throw new InvalidOperationException("registry unavailable");
        }
    }

    private sealed class SwitchableFactory : IRuntimePlacementFactory
    {
        internal bool Reject { get; set; }
        internal List<FakeHandle> Created { get; } = new List<FakeHandle>();

        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            if (Reject)
                throw new InvalidOperationException("registry unavailable");

            FakeHandle handle = new FakeHandle(placement.Id);
            Created.Add(handle);
            return handle;
        }
    }

    private sealed class FakeAdoptingFactory :
        IRuntimePlacementFactory,
        IRuntimePlacementAdopter,
        IRuntimePlacementAdoptionPass
    {
        private readonly List<FakeFrameworkMarker> _markers;
        private List<FakeFrameworkMarker> _lookup;

        internal FakeAdoptingFactory(IEnumerable<FakeFrameworkMarker> markers)
        {
            _markers = new List<FakeFrameworkMarker>(markers);
            Adopted = new List<FakeFrameworkMarker>();
        }

        internal int LookupBuildCount { get; private set; }
        internal int AdoptionPassCount { get; private set; }
        internal int AdoptionPassEndCount { get; private set; }
        internal List<FakeFrameworkMarker> Adopted { get; }

        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            return new FakeHandle(placement.Id);
        }

        public void BeginAdoptionPass()
        {
            AdoptionPassCount++;
            LookupBuildCount++;
            _lookup = new List<FakeFrameworkMarker>();
            foreach (FakeFrameworkMarker marker in _markers)
            {
                if (string.Equals(marker.Holder, "room", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(marker.Source, "Framework", StringComparison.OrdinalIgnoreCase))
                    _lookup.Add(marker);
            }
        }

        public void EndAdoptionPass()
        {
            AdoptionPassEndCount++;
            _lookup = null;
        }

        public bool TryAdopt(
            PlacementRecord placement,
            out IRuntimePlacementHandle handle,
            out string error)
        {
            handle = null;
            error = null;
            for (int index = 0; index < _lookup.Count; index++)
            {
                FakeFrameworkMarker marker = _lookup[index];
                if (!string.Equals(marker.PlacementId, placement.Id, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(marker.Type, placement.Type, StringComparison.OrdinalIgnoreCase))
                    continue;

                _lookup.RemoveAt(index);
                marker.AdoptionCount++;
                Adopted.Add(marker);
                handle = new FakeHandle(placement.Id);
                return true;
            }

            return false;
        }
    }

    private sealed class FakeFrameworkMarker
    {
        internal FakeFrameworkMarker(string holder, string source, string placementId, string type)
        {
            Holder = holder;
            Source = source;
            PlacementId = placementId;
            Type = type;
        }

        internal string Holder { get; }
        internal string Source { get; }
        internal string PlacementId { get; }
        internal string Type { get; }
        internal int AdoptionCount { get; set; }
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
