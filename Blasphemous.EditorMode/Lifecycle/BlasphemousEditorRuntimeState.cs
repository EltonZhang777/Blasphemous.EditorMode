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
        PhysicsSwitcher physics = penitent.IsAlive() ? penitent.Physics : null;
        SmartPlatformCollider collider = physics.IsAlive() ? physics.Collider : null;

        return new EditorRuntimeState(
            logic == null || logic.CurrentLevelConfig == null
                ? Time.timeScale
                : logic.CurrentLevelConfig.TimeScale,
            logic != null && logic.IsPaused,
            input != null && input.HasBlocker(InputBlocker),
            ui != null && ui.ShowGamePlayUI,
            ui != null && ui.ShowGamePlayUIForDebug,
            !penitent.IsAlive() || !penitent.PlatformCharacterController.IsAlive() || penitent.PlatformCharacterController.enabled,
            !penitent.IsAlive() || !penitent.DamageArea.IsAlive() || penitent.DamageArea.enabled,
            penitent.IsAlive() && penitent.Status.Invulnerable,
            !collider.IsAlive() || collider.enabled,
            !collider.IsAlive() || collider.EnableCollision2D,
            ui != null && ui.Cinematic.IsAlive() && ui.Cinematic.InCinematicMode,
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
        if (!penitent.IsAlive())
            return;

        if (penitent.PlatformCharacterController.IsAlive())
            penitent.PlatformCharacterController.enabled = state.PlayerControlEnabled;
        if (penitent.DamageArea.IsAlive())
            penitent.DamageArea.enabled = state.DamageAreaEnabled;
        penitent.Status.Invulnerable = state.Invulnerable;

        PhysicsSwitcher physics = penitent.Physics;
        if (physics.IsAlive())
        {
            physics.EnablePhysics(state.PlayerControlEnabled);
            physics.EnableColliders(state.CollisionEnabled);
            physics.Enable2DCollision(state.Collision2DEnabled);
        }
    }
}