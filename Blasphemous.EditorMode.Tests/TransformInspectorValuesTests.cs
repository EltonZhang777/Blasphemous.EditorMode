using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Editing;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class TransformInspectorValuesTests
{
    [Fact]
    public void EditingPositionChangesOnlyPosition()
    {
        AssertOnlyEditedGroupChanges(
            TransformGroup.Position,
            new TransformInspectorGroup("4", "5", "6"),
            new PlacementTransform(4f, 5f, 6f, 10f, 20f, 30f, 2f, 3f, 4f));
    }

    [Fact]
    public void EditingRotationChangesOnlyRotation()
    {
        AssertOnlyEditedGroupChanges(
            TransformGroup.Rotation,
            new TransformInspectorGroup("40", "50", "60"),
            new PlacementTransform(1f, 2f, 3f, 40f, 50f, 60f, 2f, 3f, 4f));
    }

    [Fact]
    public void EditingScaleChangesOnlyScale()
    {
        AssertOnlyEditedGroupChanges(
            TransformGroup.Scale,
            new TransformInspectorGroup("7", "8", "9"),
            new PlacementTransform(1f, 2f, 3f, 10f, 20f, 30f, 7f, 8f, 9f));
    }

    [Fact]
    public void AllThreeGroupsRoundTripThroughInspectorValues()
    {
        PlacementTransform original = TransformInspectorTestData.CreateRoundTrip();

        TransformInspectorValues values = TransformInspectorValues.FromTransform(original);

        Assert.True(values.TryCreateTransform(out PlacementTransform roundTripped, out string error), error);
        Assert.Equal(original, roundTripped);
    }

    [Theory]
    [InlineData("position", "not-a-number")]
    [InlineData("rotation", "NaN")]
    [InlineData("scale", "Infinity")]
    public void InvalidValueInAnyGroupFailsWithoutPartialTransform(string group, string invalidValue)
    {
        TransformInspectorGroup invalid = new(invalidValue, "4", "5");
        TransformInspectorValues values = group switch
        {
            "position" => new TransformInspectorValues(
                invalid,
                new TransformInspectorGroup("10", "20", "30"),
                new TransformInspectorGroup("2", "3", "4")),
            "rotation" => new TransformInspectorValues(
                new TransformInspectorGroup("1", "2", "3"),
                invalid,
                new TransformInspectorGroup("2", "3", "4")),
            "scale" => new TransformInspectorValues(
                new TransformInspectorGroup("1", "2", "3"),
                new TransformInspectorGroup("10", "20", "30"),
                invalid),
            _ => throw new System.ArgumentOutOfRangeException(nameof(group), group, null)
        };

        Assert.False(values.TryCreateTransform(out PlacementTransform transform, out string error));
        Assert.Null(transform);
        Assert.False(string.IsNullOrEmpty(error));
    }

    private static void AssertOnlyEditedGroupChanges(
        TransformGroup group,
        TransformInspectorGroup editedGroup,
        PlacementTransform expected)
    {
        PlacementTransform original = TransformInspectorTestData.CreateDefault();
        TransformInspectorValues values = new(
            group == TransformGroup.Position
                ? editedGroup
                : TransformInspectorGroup.FromValues(
                    original.PositionX,
                    original.PositionY,
                    original.PositionZ),
            group == TransformGroup.Rotation
                ? editedGroup
                : TransformInspectorGroup.FromValues(
                    original.RotationX,
                    original.RotationY,
                    original.RotationZ),
            group == TransformGroup.Scale
                ? editedGroup
                : TransformInspectorGroup.FromValues(
                    original.ScaleX,
                    original.ScaleY,
                    original.ScaleZ));

        Assert.True(values.TryCreateTransform(out PlacementTransform actual, out string error), error);
        Assert.Equal(expected, actual);
    }

    private enum TransformGroup
    {
        Position,
        Rotation,
        Scale
    }
}