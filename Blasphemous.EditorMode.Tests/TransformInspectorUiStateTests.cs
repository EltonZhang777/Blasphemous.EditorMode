using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Editing;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class TransformInspectorUiStateTests
{
    [Fact]
    public void SameIdAndSameTransformDoesNotRequireRefresh()
    {
        PlacementTransform displayed = CreateTransform();
        PlacementTransform selected = CreateTransform();

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
            CreateTransform(),
            "placement-1",
            CreateTransform(45f));

        Assert.False(decision.SelectionChanged);
        Assert.True(decision.FieldsRequireRefresh);
    }

    [Fact]
    public void ChangedIdRequiresRefreshEvenWhenTransformMatches()
    {
        TransformInspectorRefreshDecision decision =
            TransformInspectorRefreshPolicy.Evaluate(
            "placement-1",
            CreateTransform(),
            "placement-2",
            CreateTransform());

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

    private static PlacementTransform CreateTransform(float rotationX = 30f)
    {
        return new PlacementTransform(
            1f,
            2f,
            3f,
            10f,
            20f,
            rotationX,
            2f,
            3f,
            4f);
    }
}