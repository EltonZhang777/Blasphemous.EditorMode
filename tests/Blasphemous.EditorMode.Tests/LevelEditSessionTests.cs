using Blasphemous.EditorMode.Content;
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
