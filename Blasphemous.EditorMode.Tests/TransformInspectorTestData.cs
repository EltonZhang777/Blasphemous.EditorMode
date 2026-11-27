using Blasphemous.EditorMode.Components;
using Blasphemous.EditorMode.Editing;

namespace Blasphemous.EditorMode.Tests;

internal static class TransformInspectorTestData
{
    internal static PlacementTransform CreateDefault(float rotationZ = 30f)
    {
        return new PlacementTransform(
            1f,
            2f,
            3f,
            10f,
            20f,
            rotationZ,
            2f,
            3f,
            4f);
    }

    internal static PlacementTransform CreateRoundTrip()
    {
        return new PlacementTransform(
            1.25f,
            -2.5f,
            3.75f,
            10.5f,
            -20.25f,
            30.125f,
            0.5f,
            1.25f,
            2.5f);
    }
}

internal sealed class ArrayTransformInspectorFieldSource : ITransformInspectorFieldSource
{
    private readonly string[][] _groups;

    internal ArrayTransformInspectorFieldSource(
        string[] position,
        string[] rotation,
        string[] scale)
    {
        _groups = [position, rotation, scale];
    }

    string ITransformInspectorFieldSource.ReadField(
        TransformInspectorFieldGroup group,
        int axis)
    {
        return _groups[(int)group][axis];
    }
}