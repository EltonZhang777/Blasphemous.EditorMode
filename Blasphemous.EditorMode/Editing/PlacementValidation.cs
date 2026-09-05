using Blasphemous.EditorMode.Content;
using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Framework;

namespace Blasphemous.EditorMode.Editing;

/// <summary>Single validation boundary shared by import and runtime edits.</summary>
internal static class PlacementValidation
{
    internal static bool Validate(
        FrameworkObjectData data,
        CreatorMetadata creator,
        ApplyReport report)
    {
        return Validate(
            PlacementRecord.FromFramework(data),
            creator,
            report);
    }

    internal static bool Validate(
        PlacementRecord placement,
        CreatorMetadata creator,
        ApplyReport report)
    {
        if (placement == null || creator == null || !creator.IsSupported)
        {
            report.Error("PLACEMENT_INVALID", placement == null ? "$" : placement.Id, "Placement or creator is invalid.");
            return false;
        }

        bool valid = true;
        if (!HasTransform(creator.TransformCapabilities, TransformCapabilities.Position)
            && !IsZero(placement.Transform.PositionX, placement.Transform.PositionY, placement.Transform.PositionZ))
        {
            report.Error("TRANSFORM_UNSUPPORTED", placement.Id, "Creator does not support position editing.");
            valid = false;
        }
        if (!HasTransform(creator.TransformCapabilities, TransformCapabilities.Rotation)
            && !IsZero(placement.Transform.RotationX, placement.Transform.RotationY, placement.Transform.RotationZ))
        {
            report.Error("TRANSFORM_UNSUPPORTED", placement.Id, "Creator does not support rotation editing.");
            valid = false;
        }
        if (!HasTransform(creator.TransformCapabilities, TransformCapabilities.Scale)
            && !IsOne(placement.Transform.ScaleX, placement.Transform.ScaleY, placement.Transform.ScaleZ))
        {
            report.Error("TRANSFORM_UNSUPPORTED", placement.Id, "Creator does not support scale editing.");
            valid = false;
        }

        foreach (string property in placement.Properties)
        {
            bool supported = false;
            foreach (string supportedKey in creator.PropertyKeys)
            {
                if (string.Equals(property, supportedKey, System.StringComparison.OrdinalIgnoreCase))
                {
                    supported = true;
                    break;
                }
            }

            if (!supported)
            {
                report.Error(
                    "PROPERTY_UNSUPPORTED",
                    placement.Id,
                    "Creator does not support property '" + property + "'.");
                valid = false;
            }
        }

        return valid;
    }

    internal static bool ValidateTransform(
        PlacementTransform transform,
        CreatorMetadata creator,
        string location,
        ApplyReport report)
    {
        if (transform == null)
        {
            report.Error("TRANSFORM_INVALID", location, "Transform is required.");
            return false;
        }

        if (!AreFinite(
            transform.PositionX,
            transform.PositionY,
            transform.PositionZ,
            transform.RotationX,
            transform.RotationY,
            transform.RotationZ,
            transform.ScaleX,
            transform.ScaleY,
            transform.ScaleZ))
        {
            report.Error("TRANSFORM_INVALID", location, "Transform values must be finite.");
            return false;
        }

        return Validate(
            new PlacementRecord(
                "validation",
                creator.Type,
                transform,
                new string[0],
                PlacementOwnership.ArchitectOwned),
            creator,
            report);
    }

    private static bool HasTransform(TransformCapabilities value, TransformCapabilities flag)
    {
        return (value & flag) == flag;
    }

    private static bool IsZero(float x, float y, float z)
    {
        return x == 0f && y == 0f && z == 0f;
    }

    private static bool IsOne(float x, float y, float z)
    {
        return x == 1f && y == 1f && z == 1f;
    }

    private static bool AreFinite(params float[] values)
    {
        foreach (float value in values)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;
        }

        return true;
    }
}
