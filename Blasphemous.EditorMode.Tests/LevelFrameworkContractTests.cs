using System.Linq;
using Blasphemous.EditorMode.Framework;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class LevelFrameworkContractTests
{
    [Fact]
    public void AdditionContractMatchesFrameworkObjectData()
    {
        Assert.Equal(
            new[] { "type", "id", "position", "rotation", "scale", "condition", "properties" },
            LevelFrameworkContract.RequiredFields(FrameworkOperation.Addition));
    }

    [Fact]
    public void ModificationAndDeletionRequireScenePath()
    {
        Assert.Contains("scene", LevelFrameworkContract.RequiredFields(FrameworkOperation.Modification));
        Assert.Contains("path", LevelFrameworkContract.RequiredFields(FrameworkOperation.Modification));
        Assert.Equal(
            new[] { "scene", "path" },
            LevelFrameworkContract.RequiredFields(FrameworkOperation.Deletion));
    }

    [Fact]
    public void UnknownFieldsAreNotPartOfCanonicalObjectData()
    {
        Assert.True(LevelFrameworkContract.IsKnownObjectField("properties"));
        Assert.False(LevelFrameworkContract.IsKnownObjectField("editorMetadata"));
        Assert.False(LevelFrameworkContract.IsKnownObjectField("version"));
    }
}
