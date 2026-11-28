using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Editing;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class TransformInspectorFieldMappingTests
{
    [Fact]
    public void ReadValuesMapsPositionRotationAndScaleFieldGroups()
    {
        string[] position = ["1", "2", "3"];
        string[] rotation = ["4", "5", "6"];
        string[] scale = ["7", "8", "9"];

        TransformInspectorValues values = TransformInspectorFieldMapping.ReadValues(
            index => position[index],
            index => rotation[index],
            index => scale[index]);

        Assert.Equal(["1", "2", "3"], [values.Position.X, values.Position.Y, values.Position.Z]);
        Assert.Equal(["4", "5", "6"], [values.Rotation.X, values.Rotation.Y, values.Rotation.Z]);
        Assert.Equal(["7", "8", "9"], [values.Scale.X, values.Scale.Y, values.Scale.Z]);
    }

    [Fact]
    public void WriteValuesMapsPositionRotationAndScaleFieldGroups()
    {
        string[] position = new string[3];
        string[] rotation = new string[3];
        string[] scale = new string[3];
        TransformInspectorValues values = new(
            new TransformInspectorGroup("1", "2", "3"),
            new TransformInspectorGroup("4", "5", "6"),
            new TransformInspectorGroup("7", "8", "9"));

        TransformInspectorFieldMapping.WriteValues(
            values,
            (index, text) => position[index] = text,
            (index, text) => rotation[index] = text,
            (index, text) => scale[index] = text);

        Assert.Equal(["1", "2", "3"], position);
        Assert.Equal(["4", "5", "6"], rotation);
        Assert.Equal(["7", "8", "9"], scale);
    }

    [Fact]
    public void TransposedFieldGroupsProduceDifferentTransform()
    {
        string[] position = ["1", "2", "3"];
        string[] rotation = ["4", "5", "6"];
        string[] scale = ["7", "8", "9"];
        PlacementTransform expected = new(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f);

        TransformInspectorValues transposed = TransformInspectorFieldMapping.ReadValues(
            index => position[index],
            index => scale[index],
            index => rotation[index]);

        Assert.True(transposed.TryCreateTransform(out PlacementTransform actual, out string error), error);
        Assert.NotEqual(expected, actual);
    }
}