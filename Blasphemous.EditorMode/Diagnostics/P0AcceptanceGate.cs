using System;

namespace Blasphemous.EditorMode.Diagnostics;

/// <summary>Evidence consumed by the P0 release decision.</summary>
internal sealed class P0AcceptanceEvidence
{
    internal bool SeamPassed { get; set; }
    internal bool DependencySafetyPassed { get; set; }
    internal bool RoomSafetyPassed { get; set; }
    internal bool OriginalSaveProtected { get; set; }
    internal bool RecoveryPassed { get; set; }
    internal bool NoDuplicateOrLeakPassed { get; set; }
    internal bool RealGameSmokePassed { get; set; }
}

/// <summary>Separates pure evidence from pending target-profile smoke evidence.</summary>
internal sealed class P0AcceptanceResult
{
    internal P0AcceptanceResult(ApplyReport report, bool pureChecksPassed, bool runtimePending)
    {
        Report = report;
        PureChecksPassed = pureChecksPassed;
        RuntimePending = runtimePending;
    }

    internal ApplyReport Report { get; }
    internal bool PureChecksPassed { get; }
    internal bool RuntimePending { get; }
    internal bool ReleaseReady { get { return PureChecksPassed && !RuntimePending; } }
    internal string StatusMessage { get { return Report.ToSummary("P0 acceptance"); } }
}

/// <summary>Evaluates the P0 gate without treating skipped smoke tests as passed.</summary>
internal static class P0AcceptanceGate
{
    internal static P0AcceptanceResult Evaluate(P0AcceptanceEvidence evidence)
    {
        if (evidence == null)
            throw new ArgumentNullException(nameof(evidence));

        ApplyReport report = new();
        Check(report, "SEAM", evidence.SeamPassed, "LevelEditSession import/edit/undo/redo/export/re-import seam");
        Check(report, "DEPENDENCY", evidence.DependencySafetyPassed, "dependency absence and version safety");
        Check(report, "ROOM_SAFETY", evidence.RoomSafetyPassed, "room identity and lifecycle safety");
        Check(report, "ORIGINAL_SAVE", evidence.OriginalSaveProtected, "Original Save protection");
        Check(report, "RECOVERY", evidence.RecoveryPassed, "failure recovery");
        Check(report, "NO_DUPLICATE", evidence.NoDuplicateOrLeakPassed, "duplicate/leak prevention");

        bool pureChecksPassed = !report.HasErrors;
        bool runtimePending = !evidence.RealGameSmokePassed;
        if (runtimePending)
        {
            report.Warning(
                "REAL_GAME_PENDING",
                "target-profile",
                "Target-game smoke evidence is pending; P0 is not release-ready.");
        }
        else
        {
            report.Info(
                "REAL_GAME_PASSED",
                "target-profile",
                "Target-game smoke evidence was supplied.");
        }

        return new P0AcceptanceResult(report, pureChecksPassed, runtimePending);
    }

    private static void Check(ApplyReport report, string code, bool passed, string description)
    {
        if (passed)
            report.Info(code + "_PASSED", code, description + " passed.");
        else
            report.Error(code + "_FAILED", code, description + " has no passing evidence.");
    }
}