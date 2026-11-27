using System.Globalization;

namespace Blasphemous.EditorMode.Lifecycle;

internal enum RoomRuntimeContextRecordKind
{
    Checked,
    Rebound,
    Failed
}

internal sealed class RoomRuntimeContextRecord
{
    private RoomRuntimeContextRecord(
        RoomRuntimeContextRecordKind kind,
        string callbackStage,
        RoomRuntimeContextEvidence evidence,
        RoomRuntimeContextDecision decision,
        string contextBefore,
        string contextAfter,
        int frame)
    {
        Kind = kind;
        CallbackStage = callbackStage;
        Level = evidence.Level;
        ActiveScene = evidence.ActiveScene;
        RoomIdentity = Describe(evidence.Identity);
        InitializerState = DescribeInitializer(evidence);
        StoreComponentState = evidence.StoreComponentPresent ? "present" : "missing";
        StoreValueState = evidence.StoreValueInitialized ? "initialized" : "uninitialized";
        ContextBefore = Normalize(contextBefore);
        ContextAfter = Normalize(contextAfter);
        State = decision.State;
        Reason = decision.Reason;
        Frame = frame;
    }

    internal RoomRuntimeContextRecordKind Kind { get; }
    internal string CallbackStage { get; }
    internal string Level { get; }
    internal string ActiveScene { get; }
    internal string RoomIdentity { get; }
    internal string InitializerState { get; }
    internal string StoreComponentState { get; }
    internal string StoreValueState { get; }
    internal string ContextBefore { get; }
    internal string ContextAfter { get; }
    internal RoomRuntimeContextState State { get; }
    internal string Reason { get; }
    internal int Frame { get; }

    internal static RoomRuntimeContextRecord Create(
        RoomRuntimeContextRecordKind kind,
        string callbackStage,
        RoomRuntimeContextEvidence evidence,
        RoomRuntimeContextDecision decision,
        string contextBefore,
        string contextAfter,
        int frame)
    {
        return new RoomRuntimeContextRecord(
            kind,
            Normalize(callbackStage),
            evidence,
            decision,
            contextBefore,
            contextAfter,
            frame);
    }

    internal string ToLogLine()
    {
        return string.Join(
            " ",
            [
                "[ROOM_CONTEXT]",
                "kind=" + KindCode(Kind),
                "stage=" + CallbackStage,
                "level=" + Normalize(Level),
                "scene=" + Normalize(ActiveScene),
                "room_identity=" + RoomIdentity,
                "initializer=" + InitializerState,
                "store_component=" + StoreComponentState,
                "store_value=" + StoreValueState,
                "context_before=" + ContextBefore,
                "context_after=" + ContextAfter,
                "state=" + StateCode(State),
                "reason=" + Reason,
                "frame=" + Frame.ToString(CultureInfo.InvariantCulture)
            ]);
    }

    private static string Describe(RoomIdentity identity)
    {
        if (identity == null)
        {
            return "<none>";
        }

        return identity.FrameworkLevelKey
            + "|" + identity.TargetSceneSuffix
            + "|" + identity.ActiveSceneName
            + "|" + identity.ExportFileKey;
    }

    private static string DescribeInitializer(RoomRuntimeContextEvidence evidence)
    {
        if (!evidence.InitializerPresent)
        {
            return "missing";
        }

        return evidence.InitializerActiveAndEnabled ? "active" : "inactive";
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrEmpty(value) ? "<none>" : value;
    }

    private static string KindCode(RoomRuntimeContextRecordKind kind)
    {
        switch (kind)
        {
            case RoomRuntimeContextRecordKind.Rebound:
                return "rebound";
            case RoomRuntimeContextRecordKind.Failed:
                return "failed";
            default:
                return "checked";
        }
    }

    private static string StateCode(RoomRuntimeContextState state)
    {
        switch (state)
        {
            case RoomRuntimeContextState.NotApplicable:
                return "not_applicable";
            case RoomRuntimeContextState.Failed:
                return "failed";
            case RoomRuntimeContextState.ObservedNotReady:
                return "observed_not_ready";
            default:
                return "ready";
        }
    }
}