using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
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
            ApplyReportAssert.MatchesCodes(result.Report, "ROOM_SCOPE");
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
            Assert.Equal(
                2,
                result.Report.Entries.Count(x => x.Code == "ADDITION_UNEDITABLE_PRESERVED"));
            Assert.False(result.Report.HasErrors, result.StatusMessage);
            Assert.Contains(
                result.Report.Entries,
                x => x.Code == "CREATOR_UNSUPPORTED"
                    && x.Severity == ApplySeverity.Warning
                    && x.Skipped);
            Assert.Contains(
                result.Report.Entries,
                x => x.Code == "PROPERTY_UNSUPPORTED"
                    && x.Severity == ApplySeverity.Warning
                    && x.Skipped);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Import_keeps_compatible_additions_when_another_addition_is_malformed()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"valid\"},{\"type\":\"bell-iron\",\"id\":\"malformed\",\"properties\":\"not-an-array\"}],\"modifications\":[],\"deletions\":[]}";
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
            Assert.False(result.Report.HasErrors, result.StatusMessage);
            Assert.Contains(
                result.Report.Entries,
                x => x.Location == "additions[1].properties"
                    && x.Severity == ApplySeverity.Warning
                    && x.Skipped);
            Assert.Contains(
                result.Report.Entries,
                x => x.Code == "ADDITION_UNEDITABLE_PRESERVED"
                    && x.Location == "additions[malformed]");
            Assert.Equal(source, File.ReadAllText(path));

            LevelEditSessionExportResult exported = result.Session.Export(path);

            Assert.False(exported.Succeeded);
            Assert.Equal(source, File.ReadAllText(path));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Import_keeps_the_first_compatible_addition_when_ids_collide_by_case()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"A\"},{\"type\":\"bell-iron\",\"id\":\"a\"}],\"modifications\":[],\"deletions\":[]}";
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
            Assert.Equal("A", result.Session.Placements[0].Id);
            Assert.False(result.Report.HasErrors, result.StatusMessage);
            Assert.Contains(
                result.Report.Entries,
                x => x.Code == "PLACEMENT_DUPLICATE_ID"
                    && x.Severity == ApplySeverity.Warning
                    && x.Skipped);
            Assert.False(result.Session.ImportedDocument.Additions[0].PreserveOriginalAddition);
            Assert.True(result.Session.ImportedDocument.Additions[1].PreserveOriginalAddition);

            LevelEditSessionExportResult exported = result.Session.Export(path);

            Assert.True(exported.Succeeded, exported.StatusMessage);
            JArray additions = (JArray)JObject.Parse(File.ReadAllText(path))["additions"];
            Assert.Equal(new[] { "A", "a" }, additions.Select(x => (string)x["id"]));
            Assert.Equal(2, result.Session.ImportedDocument.Additions.Count);

            FrameworkFileResult reimportedFile = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            LevelEditSessionImportResult reimported = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                reimportedFile,
                new[] { BaseCreatorCatalog.BellIron });

            Assert.True(reimported.Succeeded, reimported.StatusMessage);
            Assert.Single(reimported.Session.Placements);
            Assert.Equal("A", reimported.Session.Placements[0].Id);
            Assert.False(reimported.Report.HasErrors, reimported.StatusMessage);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Preserved_addition_ids_still_claim_case_insensitive_identity()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"A\",\"properties\":[\"unsupported\"]},{\"type\":\"bell-iron\",\"id\":\"a\"}],\"modifications\":[],\"deletions\":[]}";
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
            Assert.Empty(result.Session.Placements);
            Assert.False(result.Report.HasErrors, result.StatusMessage);
            Assert.All(
                result.Session.ImportedDocument.Additions,
                addition => Assert.True(addition.PreserveOriginalAddition));
            Assert.Contains(
                result.Report.Entries,
                x => x.Code == "PLACEMENT_DUPLICATE_ID"
                    && x.Location == "additions[a]"
                    && x.Severity == ApplySeverity.Warning
                    && x.Skipped);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void New_placement_ids_do_not_collide_with_preserved_additions()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"bell-iron-1\",\"properties\":[\"unsupported\"]}],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            LevelEditSessionImportResult imported = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file,
                new[] { BaseCreatorCatalog.BellIron });
            RuntimePlacementManager manager = new(
                imported.Session,
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new TestFactory());
            PlacementRecord collision = new(
                "bell-iron-1",
                BaseCreatorCatalog.BellIronType,
                PlacementTransform.Default,
                [],
                PlacementOwnership.ArchitectOwned);

            ApplyReport duplicateReport = new();
            Assert.False(imported.Session.TryAddPlacement(collision, duplicateReport));
            Assert.Contains(
                duplicateReport.Entries,
                entry => entry.Code == "PLACEMENT_DUPLICATE_ID");

            ApplyReport placeReport = new();
            Assert.True(
                manager.TryPlace(
                    BaseCreatorCatalog.BellIronType,
                    PlacementTransform.Default,
                    [],
                    placeReport,
                    out PlacementRecord placement),
                placeReport.ToSummary("place"));
            Assert.Equal("bell-iron-2", placement.Id);

            LevelEditSessionExportResult exported = imported.Session.Export(path);

            Assert.True(exported.Succeeded, exported.StatusMessage);
            JArray additions = (JArray)JObject.Parse(File.ReadAllText(path))["additions"];
            Assert.Equal(
                new[] { "bell-iron-1", "bell-iron-2" },
                additions.Select(x => (string)x["id"]));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Import_does_not_downgrade_a_duplicate_id_error()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"same\"},{\"type\":\"bell-iron\",\"id\":\"same\"}],\"modifications\":[],\"deletions\":[]}";
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
            Assert.True(result.Report.HasErrors, result.StatusMessage);
            Assert.Contains(
                result.Report.Entries,
                x => x.Code == "PLACEMENT_DUPLICATE_ID"
                    && x.Severity == ApplySeverity.Error);
            Assert.False(result.Session.ImportedDocument.Additions[0].PreserveOriginalAddition);
            Assert.True(result.Session.ImportedDocument.Additions[1].PreserveOriginalAddition);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Import_retains_original_object_operations_as_editable_round_trip_data()
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
            Assert.Single(result.Session.Modifications);
            Assert.Single(result.Session.Deletions);
            Assert.DoesNotContain(result.Report.Entries, x => x.Code == "ORIGINAL_OBJECT_READ_ONLY");
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ImportRejectsAnUneditableModificationWithoutCreatingAnExportableSession()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"valid\"}],\"modifications\":[{\"scene\":\"logic\",\"path\":\"Old\",\"type\":\"bell-iron\",\"id\":\"old\",\"condition\":\"future:value\"}],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");

            LevelEditSessionImportResult result = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file,
                new[] { BaseCreatorCatalog.BellIron });

            Assert.False(result.Succeeded);
            Assert.Null(result.Session);
            Assert.Contains(result.Report.Entries, x => x.Code == "IMPORT_INVALID");
            Assert.Equal(source, File.ReadAllText(path));
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
    public void Export_preserves_uneditable_additions_and_round_trips_supported_edits()
    {
        string directory = MakeDirectory();
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[{\"type\":\"unknown\",\"id\":\"missing\",\"payload\":{\"keep\":true}},{\"type\":\"bell-iron\",\"id\":\"unsupported\",\"properties\":[\"unsupported\"]},{\"type\":\"bell-iron\",\"id\":\"unsupported-condition\",\"condition\":\"future:value\"},{\"type\":\"bell-iron\",\"id\":\"supported\",\"position\":{\"x\":1,\"y\":2,\"z\":3}}],\"modifications\":[],\"deletions\":[]}";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);
            FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            LevelEditSessionImportResult imported = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                file,
                new[] { BaseCreatorCatalog.BellIron });

            Assert.True(imported.Succeeded, imported.StatusMessage);
            Assert.Equal(LevelEditSessionState.Partial, imported.State);
            Assert.False(imported.Report.HasErrors, imported.StatusMessage);
            Assert.Contains(imported.Report.Entries, entry => entry.Code == "CREATOR_UNSUPPORTED");
            Assert.Contains(imported.Report.Entries, entry => entry.Code == "PROPERTY_UNSUPPORTED");
            Assert.Contains(imported.Report.Entries, entry => entry.Code == "ADDITION_UNEDITABLE_PRESERVED");
            Assert.Single(imported.Session.Placements);

            RuntimePlacementManager manager = new RuntimePlacementManager(
                imported.Session,
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                new TestFactory());
            ApplyReport report = new ApplyReport();
            PlacementRecord supported = imported.Session.Placements[0];
            Assert.True(manager.TryProjectExisting(supported, report), report.ToSummary("project"));
            Assert.True(manager.TryMove(
                supported.Id,
                new PlacementTransform(8f, 2f, 3f, 0f, 0f, 0f, 1f, 1f, 1f),
                report), report.ToSummary("move"));

            LevelEditSessionExportResult exported = imported.Session.Export(path);

            Assert.True(exported.Succeeded, exported.StatusMessage);
            Assert.Equal(LevelEditSessionState.Partial, imported.Session.State);
            Assert.Contains(exported.Report.Entries, entry => entry.Code == "UNEDITABLE_ADDITION_PRESERVED");
            JObject originalJson = JObject.Parse(source);
            JObject exportedJson = JObject.Parse(File.ReadAllText(path));
            Assert.True(JToken.DeepEquals(originalJson["additions"][0], exportedJson["additions"][0]));
            Assert.True(JToken.DeepEquals(originalJson["additions"][1], exportedJson["additions"][1]));
            Assert.True(JToken.DeepEquals(originalJson["additions"][2], exportedJson["additions"][2]));

            FrameworkFileResult reimportedFile = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            LevelEditSessionImportResult reimported = LevelEditSession.Import(
                CreateRoom("D03Z02S06", "D03Z02S06_LOGIC"),
                reimportedFile,
                new[] { BaseCreatorCatalog.BellIron });

            Assert.Equal(4, reimportedFile.Document.Additions.Count);
            Assert.Equal(8f, reimported.Session.Placements[0].Transform.PositionX);
            Assert.Contains(reimported.Report.Entries, entry => entry.Code == "CREATOR_UNSUPPORTED");
            Assert.Contains(reimported.Report.Entries, entry => entry.Code == "PROPERTY_UNSUPPORTED");
            Assert.Contains(reimported.Report.Entries, entry => entry.Code == "ADDITION_UNEDITABLE_PRESERVED");
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
            TestFactory factory = new TestFactory();
            RuntimePlacementManager manager = new RuntimePlacementManager(
                imported.Session,
                ContentCatalog.FromEntries(new[] { BaseCreatorCatalog.BellIron }),
                factory);
            ApplyReport report = new ApplyReport();
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