using Blasphemous.EditorMode.Lifecycle;
using System;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RoomRuntimeContextReadinessTests
{
    [Fact]
    public void MainMenuIsNotApplicableEvenWithoutRoomEvidence()
    {
        RoomRuntimeContextEvidence evidence = new()
        {
            Level = "MainMenu"
        };

        RoomRuntimeContextDecision decision = RoomRuntimeContextReadiness.Evaluate(evidence);

        Assert.Equal(RoomRuntimeContextState.NotApplicable, decision.State);
        Assert.Equal(RoomRuntimeContextReasons.MainMenu, decision.Reason);
        Assert.Equal(RoomRuntimeContextAction.None, decision.Action);
        Assert.False(decision.IsReady);
    }

    [Fact]
    public void MissingEvidenceFailsClosedWithASpecificReason()
    {
        AssertFailure(
            evidence => evidence.Level = " ",
            RoomRuntimeContextReasons.LevelInvalid);
        AssertFailure(
            evidence => evidence.LogicAvailable = false,
            RoomRuntimeContextReasons.LogicUnavailable);
        AssertFailure(
            evidence => evidence.SceneValid = false,
            RoomRuntimeContextReasons.SceneInvalid);
        AssertFailure(
            evidence => evidence.SceneLoaded = false,
            RoomRuntimeContextReasons.SceneNotLoaded);
        AssertFailure(
            evidence => evidence.InitializerPresent = false,
            RoomRuntimeContextReasons.InitializerMissing);
        AssertFailure(
            evidence => evidence.InitializerActiveAndEnabled = false,
            RoomRuntimeContextReasons.InitializerInactive);
        AssertFailure(
            evidence => evidence.StoreComponentPresent = false,
            RoomRuntimeContextReasons.StoreComponentMissing);
    }

    [Fact]
    public void NullEvidenceFailsClosed()
    {
        RoomRuntimeContextDecision decision = RoomRuntimeContextReadiness.Evaluate(null);

        Assert.Equal(RoomRuntimeContextState.Failed, decision.State);
        Assert.Equal(RoomRuntimeContextReasons.LevelInvalid, decision.Reason);
        Assert.Equal(RoomRuntimeContextAction.None, decision.Action);
        Assert.False(decision.IsReady);
    }

    [Fact]
    public void MissingRoomIdentityAllowsStructuralRebindButIsNotReady()
    {
        RoomRuntimeContextEvidence evidence = ReadyEvidence();
        evidence.Identity = null;
        evidence.ContextReferencesInitializer = false;

        RoomRuntimeContextDecision decision = RoomRuntimeContextReadiness.Evaluate(evidence);

        Assert.Equal(RoomRuntimeContextState.Failed, decision.State);
        Assert.Equal(RoomRuntimeContextReasons.RoomIdentityUnavailable, decision.Reason);
        Assert.Equal(RoomRuntimeContextAction.Rebind, decision.Action);
        Assert.False(decision.IsReady);
    }

    [Fact]
    public void UninitializedStoreIsObservedButNeverReady()
    {
        RoomRuntimeContextEvidence evidence = ReadyEvidence();
        evidence.StoreValueInitialized = false;
        evidence.ContextReferencesInitializer = false;

        RoomRuntimeContextDecision decision = RoomRuntimeContextReadiness.Evaluate(evidence);

        Assert.Equal(RoomRuntimeContextState.ObservedNotReady, decision.State);
        Assert.Equal(RoomRuntimeContextReasons.StoreValueUninitialized, decision.Reason);
        Assert.Equal(RoomRuntimeContextAction.Rebind, decision.Action);
        Assert.False(decision.IsReady);

        evidence.ContextReferencesInitializer = true;
        decision = RoomRuntimeContextReadiness.Evaluate(evidence);
        Assert.Equal(RoomRuntimeContextState.ObservedNotReady, decision.State);
        Assert.Equal(RoomRuntimeContextAction.None, decision.Action);
        Assert.False(decision.IsReady);
    }

    [Fact]
    public void InitializedContextThatIsNotCurrentRequiresRebindBeforeReady()
    {
        RoomRuntimeContextEvidence evidence = ReadyEvidence();
        evidence.ContextReferencesInitializer = false;

        RoomRuntimeContextDecision decision = RoomRuntimeContextReadiness.Evaluate(evidence);

        Assert.Equal(RoomRuntimeContextState.ObservedNotReady, decision.State);
        Assert.Equal(RoomRuntimeContextReasons.ContextRebindRequired, decision.Reason);
        Assert.Equal(RoomRuntimeContextAction.Rebind, decision.Action);
        Assert.False(decision.IsReady);
    }

    [Fact]
    public void InitializedCurrentContextIsReady()
    {
        RoomRuntimeContextDecision decision =
            RoomRuntimeContextReadiness.Evaluate(ReadyEvidence());

        Assert.Equal(RoomRuntimeContextState.Ready, decision.State);
        Assert.Equal(RoomRuntimeContextReasons.CurrentContextInitialized, decision.Reason);
        Assert.Equal(RoomRuntimeContextAction.None, decision.Action);
        Assert.True(decision.IsReady);
    }

    [Fact]
    public void RecordContractDistinguishesCheckedReboundAndFailedRecords()
    {
        RoomRuntimeContextEvidence evidence = ReadyEvidence();
        RoomRuntimeContextRecord ready = CreateRecord(
            RoomRuntimeContextRecordKind.Checked,
            evidence,
            "MainMenu",
            "D03Z02S06#17");

        RoomRuntimeContextEvidence rebindEvidence = ReadyEvidence();
        rebindEvidence.ContextReferencesInitializer = false;
        RoomRuntimeContextRecord rebound = CreateRecord(
            RoomRuntimeContextRecordKind.Rebound,
            rebindEvidence,
            "<none>",
            "D03Z02S06#17");

        evidence.StoreComponentPresent = false;
        RoomRuntimeContextRecord failed = CreateRecord(
            RoomRuntimeContextRecordKind.Failed,
            evidence,
            "<none>",
            "<none>");

        Assert.Contains("kind=checked", ready.ToLogLine());
        Assert.Contains("kind=rebound", rebound.ToLogLine());
        Assert.Contains("kind=failed", failed.ToLogLine());
        Assert.Contains("stage=OnLevelPreloaded", rebound.ToLogLine());
        Assert.Contains("level=D03Z02S06", rebound.ToLogLine());
        Assert.Contains("scene=D03Z02S06_LOGIC", rebound.ToLogLine());
        Assert.Contains("room_identity=D03Z02S06|_LOGIC|D03Z02S06_LOGIC|D03Z02S06", rebound.ToLogLine());
        Assert.Contains("initializer=active", rebound.ToLogLine());
        Assert.Contains("store_component=present", rebound.ToLogLine());
        Assert.Contains("store_value=initialized", rebound.ToLogLine());
        Assert.Contains("state=observed_not_ready", rebound.ToLogLine());
        Assert.Contains(
            "reason=" + RoomRuntimeContextReasons.ContextRebindRequired,
            rebound.ToLogLine());
        Assert.Contains("context_before=<none>", rebound.ToLogLine());
        Assert.Contains("context_after=D03Z02S06#17", rebound.ToLogLine());
        Assert.Contains("frame=321", rebound.ToLogLine());
        Assert.Contains("store_component=missing", failed.ToLogLine());
        Assert.Contains(
            "reason=" + RoomRuntimeContextReasons.StoreComponentMissing,
            failed.ToLogLine());

        RoomRuntimeContextEvidence uninitializedEvidence = ReadyEvidence();
        uninitializedEvidence.StoreValueInitialized = false;
        RoomRuntimeContextRecord uninitialized = CreateRecord(
            RoomRuntimeContextRecordKind.Checked,
            uninitializedEvidence,
            "<none>",
            "<none>");
        Assert.Contains("store_value=uninitialized", uninitialized.ToLogLine());
        Assert.Contains("state=observed_not_ready", uninitialized.ToLogLine());
        Assert.Contains(
            "reason=" + RoomRuntimeContextReasons.StoreValueUninitialized,
            uninitialized.ToLogLine());
    }

    private static RoomRuntimeContextRecord CreateRecord(
        RoomRuntimeContextRecordKind kind,
        RoomRuntimeContextEvidence evidence,
        string contextBefore,
        string contextAfter)
    {
        return RoomRuntimeContextRecord.Create(
            kind,
            "OnLevelPreloaded",
            evidence,
            RoomRuntimeContextReadiness.Evaluate(evidence),
            contextBefore,
            contextAfter,
            321);
    }

    private static void AssertFailure(
        Action<RoomRuntimeContextEvidence> makeInvalid,
        string reason)
    {
        RoomRuntimeContextEvidence evidence = ReadyEvidence();
        makeInvalid(evidence);

        RoomRuntimeContextDecision decision = RoomRuntimeContextReadiness.Evaluate(evidence);

        Assert.Equal(RoomRuntimeContextState.Failed, decision.State);
        Assert.Equal(reason, decision.Reason);
        Assert.Equal(RoomRuntimeContextAction.None, decision.Action);
        Assert.False(decision.IsReady);
    }

    private static RoomRuntimeContextEvidence ReadyEvidence()
    {
        RoomIdentity identity;
        string error;
        Assert.True(
            RoomIdentity.TryCreate("D03Z02S06", "D03Z02S06_LOGIC", out identity, out error),
            error);

        return new RoomRuntimeContextEvidence
        {
            Level = "D03Z02S06",
            ActiveScene = "D03Z02S06_LOGIC",
            LogicAvailable = true,
            SceneValid = true,
            SceneLoaded = true,
            Identity = identity,
            InitializerPresent = true,
            InitializerActiveAndEnabled = true,
            StoreComponentPresent = true,
            StoreValueInitialized = true,
            ContextReferencesInitializer = true
        };
    }
}