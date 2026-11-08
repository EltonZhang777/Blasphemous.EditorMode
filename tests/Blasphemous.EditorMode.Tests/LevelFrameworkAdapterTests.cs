using Blasphemous.EditorMode.Framework;
using System;
using System.IO;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class LevelFrameworkAdapterTests
{
    [Fact]
    public void Parse_and_serialize_round_trip_all_framework_arrays()
    {
        const string json = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"bell-1\",\"position\":{\"x\":1,\"y\":2},\"rotation\":{\"z\":3},\"scale\":{\"x\":1,\"y\":1,\"z\":1},\"condition\":\"flag:opened\",\"properties\":[\"safe\"]}],\"modifications\":[{\"scene\":\"logic\",\"path\":\"TRAPS/Bell\",\"type\":\"bell-iron\",\"id\":\"bell-2\"}],\"deletions\":[{\"scene\":\"decoration\",\"path\":\"Props/Old\"}]}";

        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(json);
        Assert.True(parsed.IsValid, Messages(parsed));
        Assert.Single(parsed.Document.Additions);
        Assert.Single(parsed.Document.Modifications);
        Assert.Single(parsed.Document.Deletions);
        Assert.Equal(2f, parsed.Document.Additions[0].Position.Y);
        Assert.Equal(0f, parsed.Document.Additions[0].Position.Z);
        Assert.Equal("flag:opened", parsed.Document.Additions[0].Condition);
        Assert.Equal("safe", parsed.Document.Additions[0].Properties[0]);

        FrameworkCodecResult serialized = LevelFrameworkAdapter.Serialize(parsed.Document);
        Assert.True(serialized.IsValid, Messages(serialized));
        FrameworkCodecResult reparsed = LevelFrameworkAdapter.Parse(serialized.CanonicalJson);
        Assert.True(reparsed.IsValid, Messages(reparsed));
        Assert.Equal("bell-1", reparsed.Document.Additions[0].Id);
        Assert.Equal("TRAPS/Bell", reparsed.Document.Modifications[0].Path);
        Assert.Equal("Props/Old", reparsed.Document.Deletions[0].Path);
    }

    [Fact]
    public void Invalid_entries_are_skipped_individually_and_reported()
    {
        const string json = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"same\"},{\"type\":\"bell-iron\",\"id\":\"same\"},{\"type\":\"bell-iron\",\"id\":\"bad-condition\",\"condition\":\"unknown:value\"},{\"type\":\"bell-iron\",\"id\":\"bad-transform\",\"position\":{\"x\":\"not-number\"}},{\"type\":\"bell-iron\",\"id\":\"valid\"}],\"modifications\":[],\"deletions\":[]}";

        FrameworkCodecResult result = LevelFrameworkAdapter.Parse(json);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Document.Additions.Count);
        Assert.Contains(result.Diagnostics, x => x.Message.Contains("Duplicate object id"));
        Assert.Contains(result.Diagnostics, x => x.Message.Contains("Unsupported condition kind"));
        Assert.Contains(result.Diagnostics, x => x.Message.Contains("must be numeric"));
    }

    [Fact]
    public void Unknown_fields_are_warned_and_not_emitted_by_canonical_serializer()
    {
        const string json = "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"bell-1\",\"editorOnly\":true}],\"modifications\":[],\"deletions\":[],\"metadata\":{\"dirty\":true}}";

        FrameworkCodecResult parsed = LevelFrameworkAdapter.Parse(json);
        Assert.True(parsed.IsValid, Messages(parsed));
        Assert.Equal(2, parsed.Diagnostics.Count);

        FrameworkCodecResult serialized = LevelFrameworkAdapter.Serialize(parsed.Document);
        Assert.True(serialized.IsValid, Messages(serialized));
        Assert.DoesNotContain("editorOnly", serialized.CanonicalJson);
        Assert.DoesNotContain("metadata", serialized.CanonicalJson);
    }

    [Fact]
    public void Missing_arrays_and_invalid_conditions_fail_closed()
    {
        FrameworkCodecResult missing = LevelFrameworkAdapter.Parse("{\"additions\":[]}");
        Assert.False(missing.IsValid);
        Assert.Contains(missing.Diagnostics, x => x.Message.Contains("Required top-level array"));

        FrameworkCodecResult condition = LevelFrameworkAdapter.Parse(
            "{\"additions\":[{\"type\":\"bell-iron\",\"id\":\"bell\",\"condition\":\"flag:\"}],\"modifications\":[],\"deletions\":[]}");
        Assert.False(condition.IsValid);
        Assert.Contains(condition.Diagnostics, x => x.Message.Contains("Condition value cannot be blank"));
    }

    [Fact]
    public void Import_is_room_scoped_and_preserves_source_text()
    {
        string directory = Path.Combine(Path.GetTempPath(), "editor-mode-codec-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "D03Z02S06.json");
        const string source = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, source);

            FrameworkFileResult imported = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");
            Assert.True(imported.IsValid, Messages(imported.Diagnostics));
            Assert.Equal(source, imported.OriginalText);
            Assert.Equal(source, File.ReadAllText(path));

            FrameworkFileResult rejected = LevelFrameworkAdapter.ImportFile(path, "D04Z01S01");
            Assert.False(rejected.IsValid);
            Assert.Contains(rejected.Diagnostics, x => x.Message.Contains("does not match Room Identity"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Import_missing_room_file_returns_empty_document()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "editor-mode-missing-" + Guid.NewGuid().ToString("N"),
            "D03Z02S06.json");

        FrameworkFileResult imported = LevelFrameworkAdapter.ImportFile(path, "D03Z02S06");

        Assert.True(imported.IsValid, Messages(imported.Diagnostics));
        Assert.Null(imported.OriginalText);
        Assert.Empty(imported.Document.Additions);
        Assert.Empty(imported.Document.Modifications);
        Assert.Empty(imported.Document.Deletions);
    }

    [Fact]
    public void Export_validates_before_replace_and_keeps_previous_version_backup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "editor-mode-export-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "D03Z02S06.json");
        string backup = path + ".prev";
        const string oldSource = "{\"additions\":[],\"modifications\":[],\"deletions\":[]}";
        var document = new FrameworkLevelEdit(
            new[]
            {
                new FrameworkObjectData(
                    null,
                    null,
                    "bell-iron",
                    "bell-1",
                    new FrameworkVector(1f, 2f, 0f),
                    FrameworkVector.Zero,
                    FrameworkVector.One,
                    string.Empty,
                    new string[0])
            },
            new FrameworkObjectData[0],
            new FrameworkObjectData[0]);

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, oldSource);

            FrameworkExportResult exported = LevelFrameworkAdapter.ExportFile(path, "D03Z02S06", document);
            Assert.True(exported.Succeeded, Messages(exported.Diagnostics));
            Assert.Equal(backup, exported.BackupPath);
            Assert.Equal(oldSource, File.ReadAllText(backup));
            Assert.Contains("bell-1", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static string Messages(FrameworkCodecResult result)
    {
        return Messages(result.Diagnostics);
    }

    private static string Messages(System.Collections.Generic.IEnumerable<FrameworkDiagnostic> diagnostics)
    {
        var messages = new System.Collections.Generic.List<string>();
        foreach (FrameworkDiagnostic diagnostic in diagnostics)
            messages.Add(diagnostic.Message);
        return string.Join("; ", messages.ToArray());
    }
}
