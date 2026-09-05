# Blasphemous Editor Mode state guard

`EditorMode` enters only from a debug build while `BlasphemousLifecycleBridge`
is in `RoomLoaded`. The bridge changes to `Editing` for the session and keeps
that state across duplicate load callbacks.

`EditorModeGuard` captures the touched runtime state before applying the editor
target. The snapshot covers:

- level `TimeScale`, pause state, and the named `EDITOR_MODE` input blocker;
- gameplay HUD visibility flags;
- player controller, damage area, invulnerability, and both collision flags;
- cinematic state, which rejects activation instead of being modified.

Activation failure attempts an immediate restore. Exit retries one failed
restore before reporting an error. Room unload calls the same exit path before
the lifecycle bridge clears the room.

Pure guard coverage is automated. In-game entry/exit and failure-injection
smoke verification remain pending because this migration run skips real-game
tests.
