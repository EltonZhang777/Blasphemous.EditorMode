using Blasphemous.EditorMode.Diagnostics;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class P0AcceptanceGateTests
{
    [Fact]
    public void Pure_checks_can_pass_while_runtime_smoke_remains_pending()
    {
        P0AcceptanceResult result = P0AcceptanceGate.Evaluate(new P0AcceptanceEvidence
        {
            SeamPassed = true,
            DependencySafetyPassed = true,
            RoomSafetyPassed = true,
            OriginalSaveProtected = true,
            RecoveryPassed = true,
            NoDuplicateOrLeakPassed = true,
            RealGameSmokePassed = false
        });

        Assert.True(result.PureChecksPassed);
        Assert.True(result.RuntimePending);
        Assert.False(result.ReleaseReady);
        Assert.Contains(result.Report.Entries, x => x.Code == "REAL_GAME_PENDING");
    }

    [Fact]
    public void Missing_pure_evidence_blocks_the_gate_even_if_smoke_is_claimed()
    {
        P0AcceptanceResult result = P0AcceptanceGate.Evaluate(new P0AcceptanceEvidence
        {
            SeamPassed = false,
            DependencySafetyPassed = true,
            RoomSafetyPassed = true,
            OriginalSaveProtected = true,
            RecoveryPassed = true,
            NoDuplicateOrLeakPassed = true,
            RealGameSmokePassed = true
        });

        Assert.False(result.PureChecksPassed);
        Assert.False(result.RuntimePending);
        Assert.False(result.ReleaseReady);
        Assert.Contains(result.Report.Entries, x => x.Code == "SEAM_FAILED");
    }

    [Fact]
    public void Release_ready_requires_both_pure_and_real_game_evidence()
    {
        P0AcceptanceResult result = P0AcceptanceGate.Evaluate(new P0AcceptanceEvidence
        {
            SeamPassed = true,
            DependencySafetyPassed = true,
            RoomSafetyPassed = true,
            OriginalSaveProtected = true,
            RecoveryPassed = true,
            NoDuplicateOrLeakPassed = true,
            RealGameSmokePassed = true
        });

        Assert.True(result.PureChecksPassed);
        Assert.False(result.RuntimePending);
        Assert.True(result.ReleaseReady);
    }
}
