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
                if (state.Applies == state.ThrowOnApplyNumber)
                    throw new System.InvalidOperationException("injected apply failure");
                state.Current = snapshot;
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
        internal int Applies;

        internal TestState()
        {
            Current = Original;
        }
    }
}
