using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using System;
using System.IO;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class P0SeamTests
{
    [Fact]
    public void Full_p0_seam_round_trips_the_edited_room_overlay()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "editor-mode-p0-seam-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, InitialDocument());
            RoomIdentity room = CreateRoom();
            FrameworkFileResult source = LevelFrameworkAdapter.ImportFile(path, room.ExportFileKey);
            ContentCatalog catalog = ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron });
            FakeFactory factory = new FakeFactory();
            RoomOverlayReconciler reconciler = new RoomOverlayReconciler(catalog, factory);
            RoomOverlayReconcileResult first = reconciler.Apply(room, source);
            Assert.True(first.Succeeded, first.StatusMessage);

            ApplyReport report = new ApplyReport();
            PlacementRecord authored = first.Session.Placements[0];
            Assert.True(first.Manager.TrySelect(authored.Id, report, out _));
            Assert.True(first.Manager.TryMove(
                authored.Id,
                new PlacementTransform(4f, 5f, 0f, 0f, 0f, 15f, 1f, 1f, 1f),
                report));
            Assert.True(first.Manager.TryCopy(authored.Id, report, out _));
            Assert.True(first.Manager.TryPaste(report, out _));
            Assert.True(first.Manager.TryUndo(report));
            Assert.True(first.Manager.TryRedo(report));

            LevelEditSessionExportResult exported = first.Session.Export(path);
            Assert.True(exported.Succeeded, exported.StatusMessage);
            FrameworkFileResult reloadedFile = LevelFrameworkAdapter.ImportFile(path, room.ExportFileKey);
            RoomOverlayReconciler reloadedReconciler = new RoomOverlayReconciler(
                catalog,
                new FakeFactory());
            RoomOverlayReconcileResult reloaded = reloadedReconciler.Apply(room, reloadedFile);
            ApplyReport compareReport = new ApplyReport();

            Assert.True(reloaded.Succeeded, reloaded.StatusMessage);
            Assert.True(
                RoomOverlayComparer.Matches(first.Session, reloaded.Session, compareReport),
                compareReport.ToSummary("P0 seam"));
            Assert.Equal(2, reloaded.Session.Placements.Count);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static RoomIdentity CreateRoom()
    {
        RoomIdentity room;
        string error;
        Assert.True(RoomIdentity.TryCreate("D03Z02S06", "D03Z02S06_LOGIC", out room, out error), error);
        return room;
    }

    private static string InitialDocument()
    {
        return "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"authored\",\"position\":{\"x\":1,\"y\":2,\"z\":0}}],\"modifications\":[],\"deletions\":[]}";
    }

    private sealed class FakeFactory : IRuntimePlacementFactory
    {
        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            return new FakeHandle(placement.Id);
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
