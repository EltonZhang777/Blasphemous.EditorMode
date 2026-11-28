using Blasphemous.EditorMode.Diagnostics;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class P0AcceptanceGateTests
{
    [Fact]
    public void PureChecksCanPassWhileRuntimeSmokeRemainsPending()
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
    public void MissingPureEvidenceBlocksTheGateEvenIfSmokeIsClaimed()
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
    public void ReleaseReadyRequiresBothPureAndRealGameEvidence()
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
