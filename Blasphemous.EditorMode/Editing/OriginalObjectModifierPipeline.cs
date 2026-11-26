using Blasphemous.EditorMode.Diagnostics;
using Blasphemous.EditorMode.Framework;
using System;

namespace Blasphemous.EditorMode.Editing;

/// <summary>One type-specific modifier resolved for an Original Object edit.</summary>
internal interface IOriginalObjectModifier
{
    void Apply(FrameworkObjectData modification);
}

/// <summary>Resolves a Framework creator's type-specific modifier.</summary>
internal interface IOriginalObjectModifierSource
{
    bool TryCreate(
        string type,
        out IOriginalObjectModifier modifier,
        out string error);
}

/// <summary>Applies and restores one live Original Object projection.</summary>
internal interface IOriginalObjectModifierTarget
{
    OriginalObjectLiveState CaptureState();
    void ApplyBase(FrameworkObjectData modification);
    void ApplyType(
        IOriginalObjectModifier modifier,
        FrameworkObjectData modification);
    void RestoreState(OriginalObjectLiveState state);
}

/// <summary>Validates the modifier before applying BaseModifier and that modifier.</summary>
internal static class OriginalObjectModifierPipeline
{
    internal static bool TryApply(
        FrameworkObjectData modification,
        IOriginalObjectModifierSource source,
        IOriginalObjectModifierTarget target,
        ApplyReport report)
    {
        if (report == null)
            throw new ArgumentNullException(nameof(report));
        if (modification == null)
        {
            report.Error(
                ReportCodes.OriginalObjectModificationInvalid,
                "$",
                "Original Object modification is required.");
            return false;
        }

        string location = modification.Scene + ":" + modification.Path;
        if (source == null || target == null)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                location,
                "Original Object live projection is unavailable.");
            return false;
        }

        IOriginalObjectModifier modifier;
        string resolutionError;
        try
        {
            if (!source.TryCreate(modification.Type, out modifier, out resolutionError))
            {
                report.Error(
                    ReportCodes.OriginalObjectModifierUnavailable,
                    location,
                    !string.IsNullOrEmpty(resolutionError)
                        ? "Modifier '" + modification.Type + "' is unavailable: " + resolutionError
                        : "Modifier '" + modification.Type + "' is unavailable.");
                return false;
            }
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectModifierUnavailable,
                location,
                "Modifier '" + modification.Type + "' could not be resolved: " + exception.Message);
            return false;
        }

        if (modifier == null)
        {
            report.Error(
                ReportCodes.OriginalObjectModifierUnavailable,
                location,
                "Modifier '" + modification.Type + "' is unavailable.");
            return false;
        }

        OriginalObjectLiveState before;
        try
        {
            before = target.CaptureState();
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                location,
                "Original Object state could not be captured: " + exception.Message);
            return false;
        }

        try
        {
            target.ApplyBase(modification);
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectApplyFailed,
                location,
                "BaseModifier failed; the transform was rolled back: " + exception.Message);
            Restore(before, target, report, location);
            return false;
        }

        try
        {
            target.ApplyType(modifier, modification);
            return true;
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectModifierFailed,
                location,
                "Modifier '" + modification.Type + "' failed; the live state was rolled back: " + exception.Message);
            Restore(before, target, report, location);
            return false;
        }
    }

    private static void Restore(
        OriginalObjectLiveState state,
        IOriginalObjectModifierTarget target,
        ApplyReport report,
        string location)
    {
        try
        {
            target.RestoreState(state);
        }
        catch (Exception exception)
        {
            report.Error(
                ReportCodes.OriginalObjectLiveRestoreFailed,
                location,
                "The previous live state could not be restored: " + exception.Message);
        }
    }
}