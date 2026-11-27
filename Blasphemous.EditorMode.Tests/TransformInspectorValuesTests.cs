using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Editing;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class TransformInspectorValuesTests
{
    [Fact]
    public void EditingPositionChangesOnlyPosition()
    {
        PlacementTransform original = TransformInspectorTestData.CreateDefault();
        TransformInspectorValues values = new(
            new TransformInspectorGroup("4", "5", "6"),
            new TransformInspectorGroup("10", "20", "30"),
            new TransformInspectorGroup("2", "3", "4"));

        Assert.True(values.TryCreateTransform(out PlacementTransform edited, out string error), error);
        Assert.Equal(4f, edited.PositionX);
        Assert.Equal(5f, edited.PositionY);
        Assert.Equal(6f, edited.PositionZ);
        Assert.Equal(original.RotationX, edited.RotationX);
        Assert.Equal(original.RotationY, edited.RotationY);
        Assert.Equal(original.RotationZ, edited.RotationZ);
        Assert.Equal(original.ScaleX, edited.ScaleX);
        Assert.Equal(original.ScaleY, edited.ScaleY);
        Assert.Equal(original.ScaleZ, edited.ScaleZ);
    }

    [Fact]
    public void EditingRotationChangesOnlyRotation()
    {
        PlacementTransform original = TransformInspectorTestData.CreateDefault();
        TransformInspectorValues values = new(
            new TransformInspectorGroup("1", "2", "3"),
            new TransformInspectorGroup("40", "50", "60"),
            new TransformInspectorGroup("2", "3", "4"));

        Assert.True(values.TryCreateTransform(out PlacementTransform edited, out string error), error);
        Assert.Equal(original.PositionX, edited.PositionX);
        Assert.Equal(original.PositionY, edited.PositionY);
        Assert.Equal(original.PositionZ, edited.PositionZ);
        Assert.Equal(40f, edited.RotationX);
        Assert.Equal(50f, edited.RotationY);
        Assert.Equal(60f, edited.RotationZ);
        Assert.Equal(original.ScaleX, edited.ScaleX);
        Assert.Equal(original.ScaleY, edited.ScaleY);
        Assert.Equal(original.ScaleZ, edited.ScaleZ);
    }

    [Fact]
    public void EditingScaleChangesOnlyScale()
    {
        PlacementTransform original = TransformInspectorTestData.CreateDefault();
        TransformInspectorValues values = new(
            new TransformInspectorGroup("1", "2", "3"),
            new TransformInspectorGroup("10", "20", "30"),
            new TransformInspectorGroup("7", "8", "9"));

        Assert.True(values.TryCreateTransform(out PlacementTransform edited, out string error), error);
        Assert.Equal(original.PositionX, edited.PositionX);
        Assert.Equal(original.PositionY, edited.PositionY);
        Assert.Equal(original.PositionZ, edited.PositionZ);
        Assert.Equal(original.RotationX, edited.RotationX);
        Assert.Equal(original.RotationY, edited.RotationY);
        Assert.Equal(original.RotationZ, edited.RotationZ);
        Assert.Equal(7f, edited.ScaleX);
        Assert.Equal(8f, edited.ScaleY);
        Assert.Equal(9f, edited.ScaleZ);
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

}