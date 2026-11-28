using System;

namespace Blasphemous.EditorMode.Lifecycle;

internal enum RoomRuntimeContextState
{
    NotApplicable,
    Failed,
    ObservedNotReady,
    Ready
}

internal enum RoomRuntimeContextAction
{
    None,
    Rebind
}

internal static class RoomRuntimeContextReasons
{
    internal const string MainMenu = "MAIN_MENU";
    internal const string LevelInvalid = "LEVEL_INVALID";
    internal const string LogicUnavailable = "LOGIC_UNAVAILABLE";
    internal const string SceneInvalid = "SCENE_INVALID";
    internal const string SceneNotLoaded = "SCENE_NOT_LOADED";
    internal const string RoomIdentityUnavailable = "ROOM_IDENTITY_UNAVAILABLE";
    internal const string InitializerMissing = "INITIALIZER_MISSING";
    internal const string InitializerInactive = "INITIALIZER_INACTIVE";
    internal const string StoreComponentMissing = "STORE_COMPONENT_MISSING";
    internal const string StoreValueUninitialized = "STORE_VALUE_UNINITIALIZED";
    internal const string ContextRebindRequired = "CONTEXT_REBIND_REQUIRED";
    internal const string CurrentContextInitialized = "CURRENT_CONTEXT_INITIALIZED";
}

internal sealed class RoomRuntimeContextEvidence
{
    internal string Level { get; set; }
    internal string ActiveScene { get; set; }
    internal bool LogicAvailable { get; set; }
    internal bool SceneValid { get; set; }
    internal bool SceneLoaded { get; set; }
    internal RoomIdentity Identity { get; set; }
    internal bool InitializerPresent { get; set; }
    internal bool InitializerActiveAndEnabled { get; set; }
    internal bool StoreComponentPresent { get; set; }
    internal bool StoreValueInitialized { get; set; }
    internal bool ContextReferencesInitializer { get; set; }
}

internal sealed class RoomRuntimeContextDecision
{
    private RoomRuntimeContextDecision(
        RoomRuntimeContextState state,
        string reason,
        RoomRuntimeContextAction action)
    {
        State = state;
        Reason = reason;
        Action = action;
    }

    internal RoomRuntimeContextState State { get; }
    internal string Reason { get; }
    internal RoomRuntimeContextAction Action { get; }
    internal bool IsReady { get { return State == RoomRuntimeContextState.Ready; } }

    internal static RoomRuntimeContextDecision NotApplicable(string reason)
    {
        return new RoomRuntimeContextDecision(
            RoomRuntimeContextState.NotApplicable,
            reason,
            RoomRuntimeContextAction.None);
    }

    internal static RoomRuntimeContextDecision Failed(string reason)
    {
        return Failed(reason, RoomRuntimeContextAction.None);
    }

    internal static RoomRuntimeContextDecision Failed(
        string reason,
        RoomRuntimeContextAction action)
    {
        return new RoomRuntimeContextDecision(
            RoomRuntimeContextState.Failed,
            reason,
            action);
    }

    internal static RoomRuntimeContextDecision Observed(
        string reason,
        RoomRuntimeContextAction action)
    {
        return new RoomRuntimeContextDecision(
            RoomRuntimeContextState.ObservedNotReady,
            reason,
            action);
    }

    internal static RoomRuntimeContextDecision Ready()
    {
        return new RoomRuntimeContextDecision(
            RoomRuntimeContextState.Ready,
            RoomRuntimeContextReasons.CurrentContextInitialized,
            RoomRuntimeContextAction.None);
    }
}

internal static class RoomRuntimeContextReadiness
{
    internal static RoomRuntimeContextDecision Evaluate(RoomRuntimeContextEvidence evidence)
    {
        if (evidence == null)
        {
            return RoomRuntimeContextDecision.Failed(RoomRuntimeContextReasons.LevelInvalid);
        }

        if (string.Equals(evidence.Level, "MainMenu", StringComparison.OrdinalIgnoreCase))
        {
            return RoomRuntimeContextDecision.NotApplicable(RoomRuntimeContextReasons.MainMenu);
        }

        if (IsBlank(evidence.Level))
        {
            return RoomRuntimeContextDecision.Failed(RoomRuntimeContextReasons.LevelInvalid);
        }

        if (!evidence.LogicAvailable)
        {
            return RoomRuntimeContextDecision.Failed(RoomRuntimeContextReasons.LogicUnavailable);
        }

        if (!evidence.SceneValid)
        {
            return RoomRuntimeContextDecision.Failed(RoomRuntimeContextReasons.SceneInvalid);
        }

        if (!evidence.SceneLoaded)
        {
            return RoomRuntimeContextDecision.Failed(RoomRuntimeContextReasons.SceneNotLoaded);
        }

        if (!evidence.InitializerPresent)
        {
            return RoomRuntimeContextDecision.Failed(RoomRuntimeContextReasons.InitializerMissing);
        }

        if (!evidence.InitializerActiveAndEnabled)
        {
            return RoomRuntimeContextDecision.Failed(RoomRuntimeContextReasons.InitializerInactive);
        }

        if (!evidence.StoreComponentPresent)
        {
            return RoomRuntimeContextDecision.Failed(RoomRuntimeContextReasons.StoreComponentMissing);
        }

        RoomRuntimeContextAction action = evidence.ContextReferencesInitializer
            ? RoomRuntimeContextAction.None
            : RoomRuntimeContextAction.Rebind;

        if (evidence.Identity == null)
        {
            return RoomRuntimeContextDecision.Failed(
                RoomRuntimeContextReasons.RoomIdentityUnavailable,
                action);
        }

        if (!evidence.StoreValueInitialized)
        {
            return RoomRuntimeContextDecision.Observed(
                RoomRuntimeContextReasons.StoreValueUninitialized,
                action);
        }

        if (!evidence.ContextReferencesInitializer)
        {
            return RoomRuntimeContextDecision.Observed(
                RoomRuntimeContextReasons.ContextRebindRequired,
                RoomRuntimeContextAction.Rebind);
        }

        return RoomRuntimeContextDecision.Ready();
    }

    private static bool IsBlank(string value)
    {
        return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
    }
}