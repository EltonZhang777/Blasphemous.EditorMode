using Blasphemous.EditorMode.Lifecycle;
using Xunit;

namespace Blasphemous.EditorMode.Tests;

public sealed class RoomLifecycleTests
{
    [Fact]
    public void Room_identity_keeps_framework_key_scene_suffix_and_export_key()
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
    public void Main_menu_and_unrelated_scene_are_rejected()
    {
        RoomIdentity identity;
        string error;

        Assert.False(RoomIdentity.TryCreate("MainMenu", "MainMenu", out identity, out error));
        Assert.Contains("not a Room Overlay", error);
        Assert.False(RoomIdentity.TryCreate("D03Z02S06", "D04Z01S01_LOGIC", out identity, out error));
        Assert.Contains("does not belong", error);
    }

    [Fact]
    public void Repeated_load_is_idempotent_and_transition_cleans_previous_room()
    {
        int loaded = 0;
        int unloaded = 0;
        var bridge = new BlasphemousLifecycleBridge(
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
    public void Invalid_loaded_scene_faults_without_creating_a_room()
    {
        var bridge = new BlasphemousLifecycleBridge(null, null);

        Assert.False(bridge.OnLevelLoaded("D03Z02S06", "D04Z01S01_LOGIC"));
        Assert.Equal(RoomLifecycleState.Faulted, bridge.State);
        Assert.Null(bridge.CurrentRoom);
    }
}
