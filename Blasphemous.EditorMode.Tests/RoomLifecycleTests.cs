using Blasphemous.EditorMode.Lifecycle;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RoomLifecycleTests
{
    [Fact]
    public void RoomIdentityKeepsFrameworkKeySceneSuffixAndExportKey()
    {
        RoomIdentity identity;
        string error;

        Assert.True(RoomIdentity.TryCreate("D03Z02S06", "D03Z02S06_LOGIC", out identity, out error));
        Assert.Null(error);
        Assert.Equal("D03Z02S06", identity.FrameworkLevelKey);
        Assert.Equal("_LOGIC", identity.TargetSceneSuffix);
        Assert.Equal("D03Z02S06_LOGIC", identity.ActiveSceneName);
        Assert.Equal("D03Z02S06", identity.ExportFileKey);
    }

    [Fact]
    public void MainMenuAndUnrelatedSceneAreRejected()
    {
        RoomIdentity identity;
        string error;

        Assert.False(RoomIdentity.TryCreate("MainMenu", "MainMenu", out identity, out error));
        Assert.Contains("not a Room Overlay", error);
        BlasphemousLifecycleBridge bridge = new BlasphemousLifecycleBridge(null, null);
        Assert.False(bridge.OnLevelLoaded("MainMenu", "MainMenu"));
        Assert.Null(bridge.CurrentRoom);
        Assert.False(RoomIdentity.TryCreate("D03Z02S06", "D04Z01S01_LOGIC", out identity, out error));
        Assert.Contains("does not belong", error);
    }

    [Fact]
    public void RepeatedLoadIsIdempotentAndTransitionCleansPreviousRoom()
    {
        int loaded = 0;
        int unloaded = 0;
        BlasphemousLifecycleBridge bridge = new BlasphemousLifecycleBridge(
            room => loaded++,
            room => unloaded++);

        bridge.OnLevelPreloaded("MainMenu", "D03Z02S06");
        Assert.Equal(RoomLifecycleState.Transitioning, bridge.State);
        Assert.True(bridge.OnLevelLoaded("D03Z02S06", "D03Z02S06_LOGIC"));
        Assert.False(bridge.OnLevelLoaded("D03Z02S06", "D03Z02S06_LOGIC"));
        Assert.Equal(1, loaded);
        Assert.Equal(0, unloaded);

        bridge.OnLevelPreloaded("D03Z02S06", "D04Z01S01");
        Assert.Equal(1, unloaded);
        Assert.Null(bridge.CurrentRoom);
        Assert.True(bridge.OnLevelLoaded("D04Z01S01", "D04Z01S01_DECO"));
        Assert.Equal(2, loaded);

        bridge.OnLevelUnloaded("D04Z01S01", "D03Z02S06");
        Assert.Equal(RoomLifecycleState.Transitioning, bridge.State);
    }

    [Fact]
    public void InvalidLoadedSceneFaultsWithoutCreatingARoom()
    {
        BlasphemousLifecycleBridge bridge = new BlasphemousLifecycleBridge(null, null);

        Assert.False(bridge.OnLevelLoaded("D03Z02S06", "D04Z01S01_LOGIC"));
        Assert.Equal(RoomLifecycleState.Faulted, bridge.State);
        Assert.Null(bridge.CurrentRoom);
    }

    [Fact]
    public void EditingIsOnlyAllowedForALoadedRoomAndRepeatedLoadKeepsIt()
    {
        BlasphemousLifecycleBridge bridge = new BlasphemousLifecycleBridge(null, null);
        bridge.OnLevelLoaded("D03Z02S06", "D03Z02S06_LOGIC");

        Assert.True(bridge.TryBeginEditing());
        Assert.Equal(RoomLifecycleState.Editing, bridge.State);
        Assert.False(bridge.OnLevelLoaded("D03Z02S06", "D03Z02S06_LOGIC"));
        Assert.Equal(RoomLifecycleState.Editing, bridge.State);

        bridge.EndEditing();
        Assert.Equal(RoomLifecycleState.RoomLoaded, bridge.State);
    }
}
