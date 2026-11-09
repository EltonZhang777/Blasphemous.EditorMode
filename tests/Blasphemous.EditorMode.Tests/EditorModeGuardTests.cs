using Blasphemous.EditorMode.Lifecycle;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class EditorModeGuardTests
{
    [Fact]
    public void Entry_requires_debug_gate_and_compatible_room()
    {
        var state = new TestState();
        var guard = CreateGuard(state);
        var target = CreateSnapshot();
        string error;

        Assert.False(guard.TryEnter(false, true, target, out error));
        Assert.Contains("debug", error);
        Assert.False(guard.TryEnter(true, false, target, out error));
        Assert.Contains("compatible", error);
        Assert.False(guard.IsActive);
        Assert.Equal(0, state.Applies);
    }

    [Fact]
    public void Exit_restores_every_captured_field()
    {
        var state = new TestState { Current = CreateSnapshot(1.5f, true, true, false, true, false, false, true, false, true, false, false, 2) };
        var original = state.Current;
        var guard = CreateGuard(state);
        string error;

        Assert.True(guard.TryEnter(true, true, CreateSnapshot(), out error));
        Assert.True(guard.IsActive);
        Assert.True(state.Current.CursorVisible);
        Assert.Equal(0, state.Current.CursorLockState);
        state.ThrowOnApplyNumber = 2;
        Assert.True(guard.TryExit(out error));
        Assert.False(guard.IsActive);
        Assert.Same(original, state.Current);
        Assert.False(state.Current.CursorVisible);
        Assert.Equal(2, state.Current.CursorLockState);
    }

    [Fact]
    public void Resume_restores_player_runtime_and_exit_preserves_live_gameplay_state()
    {
        var state = new TestState
        {
            Current = CreateSnapshot(0.75f, false, false, true, true, true, true, false, true, true)
        };
        var guard = CreateGuard(state);
        string error;
        EditorRuntimeState editorState = CreateSnapshot(0f, true, true, false, false, false, false, true, false, false, false, true, 0);

        Assert.True(guard.TryEnter(true, true, editorState, out error), error);
        state.Current = CreateSnapshot(1f, false, true, false, false, false, false, true, false, false, false, true, 0);

        Assert.True(guard.TryResumeGameplay(out error), error);
        Assert.True(state.Current.PlayerControlEnabled);
        Assert.True(state.Current.DamageAreaEnabled);
        Assert.False(state.Current.Invulnerable);
        Assert.True(state.Current.CollisionEnabled);
        Assert.True(state.Current.Collision2DEnabled);
        Assert.True(state.Current.EditorInputBlocked);
        Assert.False(state.Current.HudVisible);
        Assert.True(state.Current.CursorVisible);

        state.Current = CreateSnapshot(1f, false, true, false, false, true, false, true, true, true, false, true, 0);
        Assert.True(guard.TryResumeGameplay(out error), error);
        Assert.False(state.Current.DamageAreaEnabled);
        Assert.True(state.Current.Invulnerable);

        Assert.True(guard.TryExit(out error), error);
        Assert.False(guard.IsActive);
        Assert.False(state.Current.EditorInputBlocked);
        Assert.True(state.Current.HudVisible);
        Assert.True(state.Current.PlayerControlEnabled);
        Assert.False(state.Current.DamageAreaEnabled);
        Assert.True(state.Current.Invulnerable);
        Assert.True(state.Current.CollisionEnabled);
        Assert.True(state.Current.Collision2DEnabled);
    }

    [Fact]
    public void Failed_resume_restores_editor_state_and_can_be_retried()
    {
        var state = new TestState
        {
            Current = CreateSnapshot(0.75f, false, false, true, true, true, true, false, true, true)
        };
        var guard = CreateGuard(state);
        string error;
        EditorRuntimeState editorState = CreateSnapshot(0f, true, true, false, false, false, false, true, false, false, false, true, 0);
        Assert.True(guard.TryEnter(true, true, editorState, out error), error);
        state.Current = CreateSnapshot(1f, false, true, false, false, false, false, true, false, false, false, true, 0);
        state.ThrowOnApplyNumber = state.Applies + 1;

        Assert.False(guard.TryResumeGameplay(out error));
        Assert.Contains("resume failed", error);
        Assert.True(guard.IsActive);
        Assert.False(state.Current.PlayerControlEnabled);
        Assert.False(state.Current.DamageAreaEnabled);
        Assert.True(state.Current.Invulnerable);
        Assert.False(state.Current.CollisionEnabled);

        state.ThrowOnApplyNumber = 0;
        Assert.True(guard.TryResumeGameplay(out error), error);
        Assert.True(state.Current.PlayerControlEnabled);
        Assert.True(state.Current.DamageAreaEnabled);
        Assert.False(state.Current.Invulnerable);
        Assert.True(state.Current.CollisionEnabled);
    }

    [Fact]
    public void Failed_entry_rolls_back_and_remains_inactive()
    {
        var state = new TestState { ThrowOnApplyNumber = 1 };
        var guard = CreateGuard(state);
        string error;

        Assert.False(guard.TryEnter(true, true, CreateSnapshot(), out error));
        Assert.Contains("failed", error);
        Assert.False(guard.IsActive);
        Assert.Same(state.Original, state.Current);
    }

    [Fact]
    public void Failed_entry_keeps_original_snapshot_when_rollback_also_fails()
    {
        var state = new TestState { ThrowOnApplyNumbers = new[] { 1, 2, 3 } };
        var guard = CreateGuard(state);
        string error;

        Assert.False(guard.TryEnter(true, true, CreateSnapshot(), out error));
        Assert.Contains("Restore failed", error);
        Assert.True(guard.IsActive);

        state.ThrowOnApplyNumbers = System.Array.Empty<int>();
        Assert.True(guard.TryExit(out error), error);
        Assert.False(guard.IsActive);
        Assert.Same(state.Original, state.Current);
    }

    [Fact]
    public void Cinematic_state_rejects_entry_without_mutation()
    {
        var state = new TestState { Current = CreateSnapshot(cinematicMode: true) };
        var original = state.Current;
        var guard = CreateGuard(state);
        string error;

        Assert.False(guard.TryEnter(true, true, CreateSnapshot(), out error));
        Assert.Contains("cinematic", error);
        Assert.Same(original, state.Current);
        Assert.Equal(0, state.Applies);
    }

    private static EditorModeGuard CreateGuard(TestState state)
    {
        return new EditorModeGuard(
            () => state.Current,
            snapshot =>
            {
                state.Applies++;
                state.Current = snapshot;
                if (state.Applies == state.ThrowOnApplyNumber
                    || System.Array.IndexOf(state.ThrowOnApplyNumbers, state.Applies) >= 0)
                    throw new System.InvalidOperationException("injected apply failure");
            });
    }

    private static EditorRuntimeState CreateSnapshot(
        float timeScale = 1f,
        bool paused = false,
        bool editorInputBlocked = false,
        bool hudVisible = true,
        bool hudDebugVisible = true,
        bool playerControlEnabled = true,
        bool damageAreaEnabled = true,
        bool invulnerable = false,
        bool collisionEnabled = true,
        bool collision2DEnabled = true,
        bool cinematicMode = false,
        bool cursorVisible = true,
        int cursorLockState = 0)
    {
        return new EditorRuntimeState(
            timeScale,
            paused,
            editorInputBlocked,
            hudVisible,
            hudDebugVisible,
            playerControlEnabled,
            damageAreaEnabled,
            invulnerable,
            collisionEnabled,
            collision2DEnabled,
            cinematicMode,
            cursorVisible,
            cursorLockState);
    }

    private sealed class TestState
    {
        internal readonly EditorRuntimeState Original = CreateSnapshot(0.75f, false, true, false, false, false, false, true, false, true, false, false, 2);
        internal EditorRuntimeState Current;
        internal int ThrowOnApplyNumber;
        internal int[] ThrowOnApplyNumbers = System.Array.Empty<int>();
        internal int Applies;

        internal TestState()
        {
            Current = Original;
        }
    }
}
