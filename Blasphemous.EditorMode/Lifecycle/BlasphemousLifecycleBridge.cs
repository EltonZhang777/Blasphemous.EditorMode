using System;

namespace Blasphemous.EditorMode.Lifecycle;

/// <summary>Lifecycle states relevant to a Room Overlay session.</summary>
internal enum RoomLifecycleState
{
    Disabled,
    ReadyInMainMenu,
    Transitioning,
    RoomLoaded,
    Editing,
    Importing,
    Exporting,
    Applying,
    Faulted
}

/// <summary>Maps Blasphemous level callbacks to an idempotent Room Overlay state.</summary>
internal sealed class BlasphemousLifecycleBridge
{
    private readonly Action<RoomIdentity> _roomLoaded;
    private readonly Action<RoomIdentity> _roomUnloaded;
    private RoomIdentity _currentRoom;

    internal BlasphemousLifecycleBridge(
        Action<RoomIdentity> roomLoaded,
        Action<RoomIdentity> roomUnloaded)
    {
        _roomLoaded = roomLoaded;
        _roomUnloaded = roomUnloaded;
        State = RoomLifecycleState.Disabled;
    }

    internal RoomLifecycleState State { get; private set; }
    internal RoomIdentity CurrentRoom { get { return _currentRoom; } }

    internal void OnLevelPreloaded(string oldLevel, string newLevel)
    {
        ClearCurrentRoom();
        State = IsMainMenu(newLevel)
            ? RoomLifecycleState.ReadyInMainMenu
            : RoomLifecycleState.Transitioning;
    }

    internal bool OnLevelLoaded(string newLevel, string activeSceneName)
    {
        if (IsMainMenu(newLevel))
        {
            ClearCurrentRoom();
            State = RoomLifecycleState.ReadyInMainMenu;
            return false;
        }

        if (!RoomIdentity.TryCreate(newLevel, activeSceneName, out RoomIdentity identity, out string error))
        {
            ClearCurrentRoom();
            State = RoomLifecycleState.Faulted;
            return false;
        }

        if (_currentRoom != null && _currentRoom.Equals(identity))
        {
            if (State != RoomLifecycleState.Editing)
                State = RoomLifecycleState.RoomLoaded;
            return false;
        }

        ClearCurrentRoom();
        _currentRoom = identity;
        State = RoomLifecycleState.RoomLoaded;
        _roomLoaded?.Invoke(identity);
        return true;
    }

    internal bool TryBeginEditing()
    {
        if (State != RoomLifecycleState.RoomLoaded || _currentRoom == null)
            return false;

        State = RoomLifecycleState.Editing;
        return true;
    }

    internal void EndEditing()
    {
        if (State == RoomLifecycleState.Editing && _currentRoom != null)
            State = RoomLifecycleState.RoomLoaded;
    }

    internal void OnLevelUnloaded(string oldLevel, string newLevel)
    {
        if (_currentRoom != null
            && string.Equals(_currentRoom.FrameworkLevelKey, oldLevel, StringComparison.OrdinalIgnoreCase))
            ClearCurrentRoom();

        State = IsMainMenu(newLevel)
            ? RoomLifecycleState.ReadyInMainMenu
            : RoomLifecycleState.Transitioning;
    }

    private void ClearCurrentRoom()
    {
        if (_currentRoom == null)
            return;

        RoomIdentity room = _currentRoom;
        _currentRoom = null;
        _roomUnloaded?.Invoke(room);
    }

    private static bool IsMainMenu(string level)
    {
        return string.Equals(level, "MainMenu", StringComparison.OrdinalIgnoreCase);
    }
}