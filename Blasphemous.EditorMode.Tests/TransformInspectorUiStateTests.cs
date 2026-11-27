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
    public void FieldMappingReadsIndexesAsXyz()
    {
        string[] fields = ["4", "5", "6"];

        TransformInspectorGroup group = TransformInspectorFieldMapping.Read(
            index => fields[index]);

        Assert.Equal("4", group.X);
        Assert.Equal("5", group.Y);
        Assert.Equal("6", group.Z);
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
        string[] position = ["1", "2", "3"];
        string[] rotation = ["4", "5", "6"];
        string[] scale = ["7", "8", "9"];

        TransformInspectorValues values = TransformInspectorFieldMapping.ReadValues(
            index => position[index],
            index => rotation[index],
            index => scale[index]);

        Assert.Equal("1", values.Position.X);
        Assert.Equal("5", values.Rotation.Y);
        Assert.Equal("9", values.Scale.Z);
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