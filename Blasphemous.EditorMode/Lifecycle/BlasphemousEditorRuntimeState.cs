using CreativeSpore.SmartColliders;
using Framework.Managers;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Movement;
using UnityEngine;

namespace Blasphemous.EditorMode.Lifecycle;

/// <summary>Blasphemous adapter for the state snapshot used by the guard.</summary>
internal static class BlasphemousEditorRuntimeState
{
    internal const string InputBlocker = "EDITOR_MODE";

    internal static EditorRuntimeState Capture()
    {
        LogicManager logic = Core.Logic;
        InputManager input = Core.Input;
        UIManager ui = Core.UI;
        Penitent penitent = logic == null ? null : logic.Penitent;
        PhysicsSwitcher physics = penitent == null ? null : penitent.Physics;
        SmartPlatformCollider collider = physics == null ? null : physics.Collider;

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
            ui != null && ui.Cinematic != null && ui.Cinematic.InCinematicMode,
            Cursor.visible,
            (int)Cursor.lockState);
    }

    internal static void Apply(EditorRuntimeState state)
    {
        LogicManager logic = Core.Logic;
        if (logic != null)
        {
            state.RestorePauseAndTimeScale(
                logic.IsPaused,
                logic.PauseGame,
                logic.ResumeGame,
                timeScale =>
                {
                    if (logic.CurrentLevelConfig != null)
                        logic.CurrentLevelConfig.TimeScale = timeScale;
                    else
                        Time.timeScale = timeScale;
                });
        }
        else
            Time.timeScale = state.Paused ? state.TimeScale : 1f;

        if (Core.Input != null)
            Core.Input.SetBlocker(InputBlocker, state.EditorInputBlocked);

        if (Core.UI != null)
        {
            Core.UI.ShowGamePlayUI = state.HudVisible;
            Core.UI.ShowGamePlayUIForDebug = state.HudDebugVisible;
        }

        Cursor.lockState = (CursorLockMode)state.CursorLockState;
        Cursor.visible = state.CursorVisible;

        Penitent penitent = logic == null ? null : logic.Penitent;
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
