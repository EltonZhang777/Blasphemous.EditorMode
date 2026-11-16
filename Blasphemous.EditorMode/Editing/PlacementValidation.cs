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
            report.Error(ReportCodes.PlacementInvalid, placement == null ? "$" : placement.Id, "Placement or creator is invalid.");
            return false;
        }

        bool valid = true;
        if (!HasTransform(creator.TransformCapabilities, TransformCapabilities.Position)
            && !IsZero(placement.Transform.PositionX, placement.Transform.PositionY, placement.Transform.PositionZ))
        {
            report.Error(ReportCodes.TransformUnsupported, placement.Id, "Creator does not support position editing.");
            valid = false;
        }
        if (!HasTransform(creator.TransformCapabilities, TransformCapabilities.Rotation)
            && !IsZero(placement.Transform.RotationX, placement.Transform.RotationY, placement.Transform.RotationZ))
        {
            report.Error(ReportCodes.TransformUnsupported, placement.Id, "Creator does not support rotation editing.");
            valid = false;
        }
        if (!HasTransform(creator.TransformCapabilities, TransformCapabilities.Scale)
            && !IsOne(placement.Transform.ScaleX, placement.Transform.ScaleY, placement.Transform.ScaleZ))
        {
            report.Error(ReportCodes.TransformUnsupported, placement.Id, "Creator does not support scale editing.");
            valid = false;
        }

        foreach (string property in placement.Properties)
        {
            if (!creator.TryValidateProperty(property, out string error))
            {
                report.Error(
                    ReportCodes.PropertyUnsupported,
                    placement.Id,
                    "Creator does not support property '" + property + "': " + error);
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
            report.Error(ReportCodes.TransformInvalid, location, "Transform is required.");
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
            report.Error(ReportCodes.TransformInvalid, location, "Transform values must be finite.");
            return false;
        }

        return Validate(
            new PlacementRecord(
                "validation",
                creator.Type,
                transform,
                [],
                PlacementOwnership.ArchitectOwned),
            creator,
            report);
    }

    internal static bool ValidateCondition(
        string condition,
        string location,
        ApplyReport report)
    {
        if (string.IsNullOrEmpty(condition) || condition.Trim().Length == 0)
            return true;

        int colon = condition.IndexOf(':');
        if (colon <= 0 || colon == condition.Length - 1)
        {
            report.Error(ReportCodes.ConditionInvalid, location, "Condition must use flag:<id>, penitence:<id>, or gamemode:<id>.");
            return false;
        }

        string kind = condition.Substring(0, colon);
        if (kind != "flag" && kind != "penitence" && kind != "gamemode")
        {
            report.Error(ReportCodes.ConditionInvalid, location, "Unsupported condition kind: " + kind);
            return false;
        }
        if (condition.Substring(colon + 1).Trim().Length == 0)
        {
            report.Error(ReportCodes.ConditionInvalid, location, "Condition value cannot be blank.");
            return false;
        }

        return true;
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