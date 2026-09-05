using System.Linq;
using Blasphemous.EditorMode.Framework;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class LevelFrameworkContractTests
{
    [Fact]
    public void Addition_contract_matches_framework_object_data()
    {
        Assert.Equal(
            new[] { "type", "id", "position", "rotation", "scale", "condition", "properties" },
            LevelFrameworkContract.RequiredFields(FrameworkOperation.Addition));
    }

    [Fact]
    public void Modification_and_deletion_require_scene_path()
    {
        Assert.Contains("scene", LevelFrameworkContract.RequiredFields(FrameworkOperation.Modification));
        Assert.Contains("path", LevelFrameworkContract.RequiredFields(FrameworkOperation.Modification));
        Assert.Equal(
            new[] { "scene", "path" },
            LevelFrameworkContract.RequiredFields(FrameworkOperation.Deletion));
    }

    [Fact]
    public void Unknown_fields_are_not_part_of_canonical_object_data()
    {
        Assert.True(LevelFrameworkContract.IsKnownObjectField("properties"));
        Assert.False(LevelFrameworkContract.IsKnownObjectField("editorMetadata"));
        Assert.False(LevelFrameworkContract.IsKnownObjectField("version"));
    }
}
