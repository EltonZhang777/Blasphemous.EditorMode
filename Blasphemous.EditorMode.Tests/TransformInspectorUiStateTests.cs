using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Editing;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class TransformInspectorUiStateTests
{
    [Fact]
    public void SameIdAndSameTransformDoesNotRequireRefresh()
    {
        PlacementTransform displayed = TransformInspectorTestData.CreateDefault();
        PlacementTransform selected = TransformInspectorTestData.CreateDefault();

        TransformInspectorRefreshDecision decision =
            TransformInspectorRefreshPolicy.Evaluate(
            "placement-1",
            displayed,
            "PLACEMENT-1",
            selected);

        Assert.False(decision.SelectionChanged);
        Assert.False(decision.FieldsRequireRefresh);
    }

    [Fact]
    public void SameIdWithChangedTransformRequiresRefresh()
    {
        TransformInspectorRefreshDecision decision =
            TransformInspectorRefreshPolicy.Evaluate(
            "placement-1",
            TransformInspectorTestData.CreateDefault(),
            "placement-1",
            TransformInspectorTestData.CreateDefault(45f));

        Assert.False(decision.SelectionChanged);
        Assert.True(decision.FieldsRequireRefresh);
    }

    [Fact]
    public void ChangedIdRequiresRefreshEvenWhenTransformMatches()
    {
        TransformInspectorRefreshDecision decision =
            TransformInspectorRefreshPolicy.Evaluate(
            "placement-1",
            TransformInspectorTestData.CreateDefault(),
            "placement-2",
            TransformInspectorTestData.CreateDefault());

        Assert.True(decision.SelectionChanged);
        Assert.True(decision.FieldsRequireRefresh);
    }

    [Fact]
    public void FieldMappingWritesXyzToIndexes()
    {
        string[] fields = new string[3];

        TransformInspectorFieldMapping.Write(
            new TransformInspectorGroup("4", "5", "6"),
            (index, text) => fields[index] = text);

        Assert.Equal("4", fields[0]);
        Assert.Equal("5", fields[1]);
        Assert.Equal("6", fields[2]);
    }

    [Fact]
    public void RefreshAppliesFieldCallbackWithoutResettingSelection()
    {
        int selectionResets = 0;
        int fieldRefreshes = 0;
        TransformInspectorRefreshDecision decision =
            TransformInspectorRefreshPolicy.Evaluate(
            "placement-1",
            TransformInspectorTestData.CreateDefault(),
            "PLACEMENT-1",
            TransformInspectorTestData.CreateDefault(45f));

        TransformInspectorRefreshPolicy.Apply(
            decision,
            () => selectionResets++,
            () => fieldRefreshes++);

        Assert.Equal(0, selectionResets);
        Assert.Equal(1, fieldRefreshes);
    }

    [Fact]
    public void ReadingTransformValuesKeepsGroupsIndependent()
    {
        ArrayTransformInspectorFieldSource source = new(
            ["1", "2", "3"],
            ["4", "5", "6"],
            ["7", "8", "9"]);

        TransformInspectorValues values = TransformInspectorFieldMapping.ReadValues(source);

        Assert.Equal(["1", "2", "3"], [values.Position.X, values.Position.Y, values.Position.Z]);
        Assert.Equal(["4", "5", "6"], [values.Rotation.X, values.Rotation.Y, values.Rotation.Z]);
        Assert.Equal(["7", "8", "9"], [values.Scale.X, values.Scale.Y, values.Scale.Z]);
    }

    [Fact]
    public void WritingTransformValuesKeepsGroupsIndependent()
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
}