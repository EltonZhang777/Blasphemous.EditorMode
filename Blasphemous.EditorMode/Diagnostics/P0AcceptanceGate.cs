using System;
using System.Collections.Generic;

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
        Check(report, P0AcceptanceCheck.Seam, evidence.SeamPassed, "LevelEditSession import/edit/undo/redo/export/re-import seam");
        Check(report, P0AcceptanceCheck.Dependency, evidence.DependencySafetyPassed, "dependency absence and version safety");
        Check(report, P0AcceptanceCheck.RoomSafety, evidence.RoomSafetyPassed, "room identity and lifecycle safety");
        Check(report, P0AcceptanceCheck.OriginalSave, evidence.OriginalSaveProtected, "Original Save protection");
        Check(report, P0AcceptanceCheck.Recovery, evidence.RecoveryPassed, "failure recovery");
        Check(report, P0AcceptanceCheck.NoDuplicate, evidence.NoDuplicateOrLeakPassed, "duplicate/leak prevention");

        bool pureChecksPassed = !report.HasErrors;
        bool runtimePending = !evidence.RealGameSmokePassed;
        if (runtimePending)
        {
            report.Warning(
                ReportCodes.RealGamePending,
                "target-profile",
                "Target-game smoke evidence is pending; P0 is not release-ready.");
        }
        else
        {
            report.Info(
                ReportCodes.RealGamePassed,
                "target-profile",
                "Target-game smoke evidence was supplied.");
        }

        return new P0AcceptanceResult(report, pureChecksPassed, runtimePending);
    }

    private static void Check(
        ApplyReport report,
        P0AcceptanceCheck check,
        bool passed,
        string description)
    {
        KeyValuePair<string, string> codeAndIdentity = ReportCodes.P0Code(check, passed);
        if (passed)
            report.Info(codeAndIdentity.Key, codeAndIdentity.Value, description + " passed.");
        else
            report.Error(codeAndIdentity.Key, codeAndIdentity.Value, description + " has no passing evidence.");
    }
}