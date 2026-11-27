using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Editing;
using Blasphemous.EditorMode.Framework;
using Blasphemous.EditorMode.Lifecycle;
using System;
using System.IO;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RawPropertiesFixtureTests
{
    private static readonly string[] FixturePropertyKeys =
    [
        "length",
        "color",
        "OneWayDown",
        "mode",
        "left",
        "right",
        "top",
        "bottom",
        "x_boundary",
        "y_boundary",
        "x_adjust",
        "y_adjust",
        "note",
        "x",
        "test-property"
    ];

    [Fact]
    public void SmokeFixtureImportsAsAnEditablePlacementWithOrderedRawProperties()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "D01Z02S01.json");
        Assert.True(File.Exists(path), path);
        FrameworkFileResult file = LevelFrameworkAdapter.ImportFile(path, "D01Z02S01");
        Assert.True(file.IsValid, Messages(file.Diagnostics));
        CreatorMetadata creator = new(
            BaseCreatorCatalog.BellIronType,
            "Iron Bell",
            "Traps",
            CreatorOrigin.BaseFramework,
            "D03Z02S06_LOGIC",
            "TRAPS/TRAP_SHOCK_CHAIN_REACTION",
            TransformCapabilities.Position | TransformCapabilities.Rotation | TransformCapabilities.Scale,
            FixturePropertyKeys);
        Assert.True(
            RoomIdentity.TryCreate("D01Z02S01", "D01Z02S01_LOGIC", out RoomIdentity room, out string error),
            error);

        LevelEditSessionImportResult result = LevelEditSession.Import(room, file, [creator]);

        Assert.True(result.Succeeded, result.StatusMessage);
        Assert.Equal(4, result.Session.Placements.Count);
        PlacementRecord placement = result.Session.Placements[2];
        Assert.Equal("bell-iron-3", placement.Id);
        Assert.Equal(25, placement.Properties.Count);
        Assert.Equal("x=1", placement.Properties[0]);
        Assert.Equal("note=length = 3.0", placement.Properties[14]);
        Assert.Equal(
            "note=horizontal-scroll-focus-0123456789abcdefghijklmnopqrstuvwxyz-ABCDEFGHIJKLMNOPQRSTUVWXYZ",
            placement.Properties[24]);
        RawPropertiesText properties = RawPropertiesText.Create(placement.Properties);
        Assert.Equal(string.Join("\n", placement.Properties), properties.Text);
        Assert.False(properties.ContainsLineBreak);
        PlacementRecord lineBreakPlacement = result.Session.Placements[3];
        Assert.Equal("bell-iron-crlf", lineBreakPlacement.Id);
        RawPropertiesText lineBreakProperties = RawPropertiesText.Create(lineBreakPlacement.Properties);
        Assert.Equal("note=first\r\nsecond", lineBreakProperties.Text);
        Assert.True(lineBreakProperties.ContainsLineBreak);
    }

#if DEBUG
    [Fact]
    public void DebugCatalogDoesNotExposeSmokeFixtureKeysWithoutActivation()
    {
        Assert.Empty(BaseCreatorCatalog.BellIron.PropertyKeys);
    }

    [Fact]
    public void RawPropertiesSmokeActivationResolvesTheFixtureKeys()
    {
        Assert.Equal(
            FixturePropertyKeys,
            RawPropertiesSmokeFixture.ResolveBellIronPropertyKeys(
                RawPropertiesSmokeFixture.ActivationValue));
    }
#endif

    private static string Messages(System.Collections.Generic.IEnumerable<FrameworkDiagnostic> diagnostics)
    {
        System.Collections.Generic.List<string> messages = [];
        foreach (FrameworkDiagnostic diagnostic in diagnostics)
        {
            messages.Add(diagnostic.Message);
        }

        return string.Join("; ", messages);
    }
}