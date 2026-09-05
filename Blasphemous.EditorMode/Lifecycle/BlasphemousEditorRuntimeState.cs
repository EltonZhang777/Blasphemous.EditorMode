using Framework.Managers;
using UnityEngine;

namespace Blasphemous.EditorMode.Lifecycle;

/// <summary>Blasphemous adapter for the state snapshot used by the guard.</summary>
internal static class BlasphemousEditorRuntimeState
{
    internal const string InputBlocker = "EDITOR_MODE";

    internal static EditorRuntimeState Capture()
    {
        var logic = Core.Logic;
        var input = Core.Input;
        var ui = Core.UI;
        var penitent = logic == null ? null : logic.Penitent;
        var physics = penitent == null ? null : penitent.Physics;
        var collider = physics == null ? null : physics.Collider;

        return new EditorRuntimeState(
            logic == null || logic.CurrentLevelConfig == null
                ? Time.timeScale
                : logic.CurrentLevelConfig.TimeScale,
            logic != null && logic.IsPaused,
            input != null && input.HasBlocker(InputBlocker),
            ui != null && ui.ShowGamePlayUI,
            ui != null && ui.ShowGamePlayUIForDebug,
            penitent == null || penitent.PlatformCharacterController == null || penitent.PlatformCharacterController.enabled,
            penitent == null || penitent.DamageArea == null || penitent.DamageArea.enabled,
            penitent != null && penitent.Status.Invulnerable,
            collider == null || collider.enabled,
            collider == null || collider.EnableCollision2D,
            ui != null && ui.Cinematic != null && ui.Cinematic.InCinematicMode);
    }

    internal static void Apply(EditorRuntimeState state)
    {
        var logic = Core.Logic;
        if (logic != null)
        {
            if (state.Paused && !logic.IsPaused)
                logic.PauseGame();
            else if (!state.Paused && logic.IsPaused)
                logic.ResumeGame();

            if (logic.CurrentLevelConfig != null)
                logic.CurrentLevelConfig.TimeScale = state.TimeScale;
        }

        if (Core.Input != null)
            Core.Input.SetBlocker(InputBlocker, state.EditorInputBlocked);

        if (Core.UI != null)
        {
            Core.UI.ShowGamePlayUI = state.HudVisible;
            Core.UI.ShowGamePlayUIForDebug = state.HudDebugVisible;
        }

        var penitent = logic == null ? null : logic.Penitent;
        if (penitent == null)
            return;

        if (penitent.PlatformCharacterController != null)
            penitent.PlatformCharacterController.enabled = state.PlayerControlEnabled;
        if (penitent.DamageArea != null)
            penitent.DamageArea.enabled = state.DamageAreaEnabled;
        penitent.Status.Invulnerable = state.Invulnerable;

        if (penitent.Physics != null)
        {
            penitent.Physics.EnablePhysics(state.PlayerControlEnabled);
            penitent.Physics.EnableColliders(state.CollisionEnabled);
            penitent.Physics.Enable2DCollision(state.Collision2DEnabled);
        }
    }
}
