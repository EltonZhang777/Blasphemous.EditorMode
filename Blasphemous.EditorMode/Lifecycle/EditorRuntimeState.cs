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

    internal EditorRuntimeState WithPlayerRuntimeFrom(EditorRuntimeState source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        return new EditorRuntimeState(
            TimeScale,
            Paused,
            EditorInputBlocked,
            HudVisible,
            HudDebugVisible,
            source.PlayerControlEnabled,
            source.DamageAreaEnabled,
            source.Invulnerable,
            source.CollisionEnabled,
            source.Collision2DEnabled,
            CinematicMode,
            CursorVisible,
            CursorLockState);
    }
}

/// <summary>Restores a captured runtime snapshot when Editor Mode exits.</summary>
internal sealed class EditorModeGuard
{
    private readonly Func<EditorRuntimeState> _capture;
    private readonly Action<EditorRuntimeState> _apply;
    private EditorRuntimeState _originalState;
    private bool _gameplayResumed;

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
            _gameplayResumed = false;
            _apply(editorState);
            IsActive = true;
            return true;
        }
        catch (Exception exception)
        {
            error = "Editor Mode activation failed: " + exception.Message;
            Exception restoreException = originalState == null ? null : TryRestore(originalState);
            if (restoreException != null)
                restoreException = TryRestore(originalState);
            error = AppendRestoreError(error, restoreException);
            _originalState = restoreException == null ? null : originalState;
            _gameplayResumed = false;
            IsActive = restoreException != null;
            return false;
        }
    }

    internal bool TryResumeGameplay(out string error)
    {
        error = null;
        if (!IsActive)
        {
            error = "Editor Mode is not active.";
            return false;
        }

        if (_gameplayResumed)
            return true;

        EditorRuntimeState current = null;
        try
        {
            current = _capture();
            if (current == null)
            {
                error = "Editor Mode could not capture the resumed game state.";
                return false;
            }

            _apply(current.WithPlayerRuntimeFrom(_originalState));
            _gameplayResumed = true;
            return true;
        }
        catch (Exception exception)
        {
            error = "Editor Mode resume failed: " + exception.Message;
            Exception restoreException = current == null ? null : TryRestore(current);
            if (restoreException != null)
                restoreException = TryRestore(current);
            error = AppendRestoreError(error, restoreException);
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
            if (_gameplayResumed)
            {
                EditorRuntimeState current = _capture();
                if (current == null)
                    throw new InvalidOperationException("Editor Mode could not capture the active player state.");
                _originalState = _originalState.WithPlayerRuntimeFrom(current);
            }

            _apply(_originalState);
            _originalState = null;
            _gameplayResumed = false;
            IsActive = false;
            return true;
        }
        catch (Exception exception)
        {
            Exception retryException = TryRestore(_originalState);
            if (retryException == null)
            {
                _originalState = null;
                _gameplayResumed = false;
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
