using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using System;
using System.IO;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class LevelEditSessionTests
{
    [Fact]
    public void Import_rejects_a_mismatched_room_before_creating_session()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, EmptyDocument());
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            RoomIdentity room = CreateRoom("D04Z01S01", "D04Z01S01_LOGIC");

            LevelEditSessionImportResult result = LevelEditSession.Import(
                room,
                file,
                new[] { BaseCreatorCatalog.BellIron });

            Assert.False(result.Succeeded);
            Assert.Null(result.Session);
            Assert.Contains(result.Report.Entries, x => x.Code == "ROOM_SCOPE");
            Assert.Equal(EmptyDocument(), File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Import_keeps_valid_owned_entries_and_reports_unsupported_entries()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"valid\"},{\"type\":\"unknown\",\"id\":\"missing\"},{\"type\":\"bell-iron\",\"id\":\"property\",\"properties\":[\"unsupported\"]}],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");

            LevelEditSessionImportResult result = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file,
                new[] { BaseCreatorCatalog.BellIron });

            Assert.True(result.Succeeded, result.StatusMessage);
            Assert.Equal(LevelEditSessionState.Partial, result.State);
            Assert.Single(result.Session.Placements);
            Assert.Equal("valid", result.Session.Placements[0].Id);
            Assert.Contains(result.Report.Entries, x => x.Code == "CREATOR_UNSUPPORTED");
            Assert.Contains(result.Report.Entries, x => x.Code == "PROPERTY_UNSUPPORTED");
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Import_retains_original_object_operations_as_read_only_round_trip_data()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[{\"scene\":\"logic\",\"path\":\"Old\",\"type\":\"bell-iron\",\"id\":\"old\"}],\"deletions\":[{\"scene\":\"deco\",\"path\":\"Gone\"}]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");

            LevelEditSessionImportResult result = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file,
                new[] { BaseCreatorCatalog.BellIron });

            Assert.True(result.Succeeded, result.StatusMessage);
            Assert.Single(result.Session.ImportedDocument.Modifications);
            Assert.Single(result.Session.ImportedDocument.Deletions);
            Assert.Contains(result.Report.Entries, x => x.Code == "ORIGINAL_OBJECT_READ_ONLY");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Export_emits_active_owned_additions_and_preserves_condition_data()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"authored\",\"condition\":\"flag:opened\"}],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            LevelEditSessionImportResult imported = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file,
                new[] { BaseCreatorCatalog.BellIron });

            LevelEditSessionExportResult exported = imported.Session.Export(path);

            Assert.True(exported.Succeeded, exported.StatusMessage);
            Assert.Contains("authored", File.ReadAllText(path));
            Assert.Contains("flag:opened", File.ReadAllText(path));
            Assert.Equal(LevelEditSessionState.Clean, imported.Session.State);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Export_does_not_reemit_a_deleted_owned_placement()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, EmptyDocument());
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            LevelEditSessionImportResult imported = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file,
                new[] { BaseCreatorCatalog.BellIron });
            var factory = new TestFactory();
            var manager = new RuntimePlacementManager(
                imported.Session,
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                factory);
            var report = new ApplyReport();
            PlacementRecord placement;
            Assert.True(manager.TryPlace(BaseCreatorCatalog.BellIronType, PlacementTransform.Default, null, report, out placement));
            Assert.True(manager.TryDelete(placement.Id, report));

            LevelEditSessionExportResult exported = imported.Session.Export(path);

            Assert.True(exported.Succeeded, exported.StatusMessage);
            Assert.DoesNotContain(placement.Id, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Export_scope_failure_preserves_the_existing_file()
    {
        string directory = MakeDirectory();
        string sourcePath = Path.Combine(directory, "D03Z02S06.json");
        string wrongPath = Path.Combine(directory, "D04Z01S01.json");
        const string oldSource = "keep-this-file";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(sourcePath, EmptyDocument());
            File.WriteAllText(wrongPath, oldSource);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(sourcePath, "D03Z02S06");
            LevelEditSessionImportResult imported = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file,
                new[] { BaseCreatorCatalog.BellIron });

            LevelEditSessionExportResult exported = imported.Session.Export(wrongPath);

            Assert.False(exported.Succeeded);
            Assert.Equal(oldSource, File.ReadAllText(wrongPath));
            Assert.Contains(exported.Report.Entries, x => x.Code == "ROOM_SCOPE");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private sealed class TestFactory : IRuntimePlacementFactory
    {
        public IRuntimePlacementHandle Create(PlacementRecord placement)
        {
            return new TestHandle(placement.Id);
        }
    }

    private sealed class TestHandle : IRuntimePlacementHandle
    {
        internal TestHandle(string id)
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

    private static RoomIdentity CreateRoom(string levelKey, string scene)
    {
        RoomIdentity room;
        string error;
        Assert.True(RoomIdentity.TryCreate(levelKey, scene, out room, out error), error);
        return room;
    }

    private static string EmptyDocument()
    {
        return "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
    }

    private static string MakeDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "editor-mode-session-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
