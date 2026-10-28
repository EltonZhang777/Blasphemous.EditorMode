using System;

namespace Blasphemous.EditorMode.Lifecycle;

/// <summary>Snapshot of game state touched while Editor Mode is active.</summary>
internal sealed class EditorRuntimeState
{
    internal EditorRuntimeState(
        float timeScale,
        bool paused,
        bool editorInputBlocked,
        bool hudVisible,
        bool hudDebugVisible,
        bool playerControlEnabled,
        bool damageAreaEnabled,
        bool invulnerable,
        bool collisionEnabled,
        bool collision2DEnabled,
        bool cinematicMode,
        bool cursorVisible,
        int cursorLockState)
    {
        TimeScale = timeScale;
        Paused = paused;
        EditorInputBlocked = editorInputBlocked;
        HudVisible = hudVisible;
        HudDebugVisible = hudDebugVisible;
        PlayerControlEnabled = playerControlEnabled;
        DamageAreaEnabled = damageAreaEnabled;
        Invulnerable = invulnerable;
        CollisionEnabled = collisionEnabled;
        Collision2DEnabled = collision2DEnabled;
        CinematicMode = cinematicMode;
        CursorVisible = cursorVisible;
        CursorLockState = cursorLockState;
    }

    internal float TimeScale { get; }
    internal bool Paused { get; }
    internal bool EditorInputBlocked { get; }
    internal bool HudVisible { get; }
    internal bool HudDebugVisible { get; }
    internal bool PlayerControlEnabled { get; }
    internal bool DamageAreaEnabled { get; }
    internal bool Invulnerable { get; }
    internal bool CollisionEnabled { get; }
    internal bool Collision2DEnabled { get; }
    internal bool CinematicMode { get; }
    internal bool CursorVisible { get; }
    internal int CursorLockState { get; }
}

/// <summary>Restores a captured runtime snapshot when Editor Mode exits.</summary>
internal sealed class EditorModeGuard
{
    private readonly Func<EditorRuntimeState> _capture;
    private readonly Action<EditorRuntimeState> _apply;
    private EditorRuntimeState _originalState;

    internal EditorModeGuard(Func<EditorRuntimeState> capture, Action<EditorRuntimeState> apply)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
    }

    internal bool IsActive { get; private set; }

    internal bool TryEnter(
        bool debugGatePassed,
        bool compatibleRoom,
        EditorRuntimeState editorState,
        out string error)
    {
        error = null;
        if (IsActive)
        {
            error = "Editor Mode is already active.";
            return false;
        }

        if (!debugGatePassed)
        {
            error = "Editor Mode requires a debug build.";
            return false;
        }

        if (!compatibleRoom)
        {
            error = "Editor Mode requires a compatible loaded room.";
            return false;
        }

        if (editorState == null)
        {
            error = "Editor Mode state target is missing.";
            return false;
        }

        EditorRuntimeState originalState = null;
        try
        {
            originalState = _capture();
            if (originalState == null)
            {
                error = "Editor Mode could not capture the current game state.";
                return false;
            }

            if (originalState.CinematicMode)
            {
                error = "Editor Mode cannot activate during a cinematic.";
                return false;
            }

            _originalState = originalState;
            _apply(editorState);
            IsActive = true;
            return true;
        }
        catch (Exception exception)
        {
            error = "Editor Mode activation failed: " + exception.Message;
            if (originalState != null)
                error = AppendRestoreError(error, TryRestore(originalState));
            _originalState = null;
            IsActive = false;
            return false;
        }
    }

    internal bool TryExit(out string error)
    {
        error = null;
        if (!IsActive)
            return true;

        try
        {
            _apply(_originalState);
            _originalState = null;
            IsActive = false;
            return true;
        }
        catch (Exception exception)
        {
            Exception retryException = TryRestore(_originalState);
            if (retryException == null)
            {
                _originalState = null;
                IsActive = false;
                error = "Editor Mode exit recovered after: " + exception.Message;
                return true;
            }

            error = "Editor Mode exit failed: " + exception.Message
                + " Restore failed: " + retryException.Message;
            return false;
        }
    }

    private string AppendRestoreError(string error, Exception restoreException)
    {
        return restoreException == null
            ? error
            : error + " Restore failed: " + restoreException.Message;
    }

    private Exception TryRestore(EditorRuntimeState state)
    {
        try
        {
            _apply(state);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
