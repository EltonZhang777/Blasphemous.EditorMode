using Blasphemous.EditorMode.Editing;
using System;
using System.Globalization;

namespace Blasphemous.EditorMode.Components;

internal sealed class TransformInspectorGroup
{
    internal TransformInspectorGroup(string x, string y, string z)
    {
        X = x ?? string.Empty;
        Y = y ?? string.Empty;
        Z = z ?? string.Empty;
    }

    internal string X { get; }
    internal string Y { get; }
    internal string Z { get; }

    internal static TransformInspectorGroup Empty
    {
        get { return new TransformInspectorGroup(string.Empty, string.Empty, string.Empty); }
    }

    internal static TransformInspectorGroup FromValues(float x, float y, float z)
    {
        return new TransformInspectorGroup(
            x.ToString("R", CultureInfo.InvariantCulture),
            y.ToString("R", CultureInfo.InvariantCulture),
            z.ToString("R", CultureInfo.InvariantCulture));
    }

    internal bool TryParse(out float x, out float y, out float z)
    {
        x = 0f;
        y = 0f;
        z = 0f;
        return TryParseAxis(X, out x)
            && TryParseAxis(Y, out y)
            && TryParseAxis(Z, out z);
    }

    private static bool TryParseAxis(string text, out float value)
    {
        if (!float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value)
            || float.IsNaN(value)
            || float.IsInfinity(value))
        {
            value = 0f;
            return false;
        }

        return true;
    }
}

internal sealed class TransformInspectorValues
{
    private const string InvalidValuesMessage = "Transform values must be finite numbers.";

    internal TransformInspectorValues(
        TransformInspectorGroup position,
        TransformInspectorGroup rotation,
        TransformInspectorGroup scale)
    {
        Position = position ?? TransformInspectorGroup.Empty;
        Rotation = rotation ?? TransformInspectorGroup.Empty;
        Scale = scale ?? TransformInspectorGroup.Empty;
    }

    internal TransformInspectorGroup Position { get; }
    internal TransformInspectorGroup Rotation { get; }
    internal TransformInspectorGroup Scale { get; }

    internal static TransformInspectorValues FromTransform(PlacementTransform transform)
    {
        if (transform == null)
        {
            throw new ArgumentNullException(nameof(transform));
        }

        return new TransformInspectorValues(
            TransformInspectorGroup.FromValues(
                transform.PositionX,
                transform.PositionY,
                transform.PositionZ),
            TransformInspectorGroup.FromValues(
                transform.RotationX,
                transform.RotationY,
                transform.RotationZ),
            TransformInspectorGroup.FromValues(
                transform.ScaleX,
                transform.ScaleY,
                transform.ScaleZ));
    }

    internal static bool ShouldRefreshTransformFields(
        string previousId,
        PlacementTransform previousTransform,
        string selectedId,
        PlacementTransform selectedTransform,
        out bool selectionChanged)
    {
        selectionChanged = !string.Equals(previousId, selectedId, StringComparison.OrdinalIgnoreCase);
        return selectionChanged || !object.Equals(previousTransform, selectedTransform);
    }

    internal bool TryCreateTransform(out PlacementTransform transform, out string error)
    {
        transform = null;
        if (!Position.TryParse(out float positionX, out float positionY, out float positionZ)
            || !Rotation.TryParse(out float rotationX, out float rotationY, out float rotationZ)
            || !Scale.TryParse(out float scaleX, out float scaleY, out float scaleZ))
        {
            error = InvalidValuesMessage;
            return false;
        }

        transform = new PlacementTransform(
            positionX,
            positionY,
            positionZ,
            rotationX,
            rotationY,
            rotationZ,
            scaleX,
            scaleY,
            scaleZ);
        error = null;
        return true;
    }
}