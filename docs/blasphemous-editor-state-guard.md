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

Activation failure attempts an immediate restore, retries once, and retains the
snapshot if both attempts fail so the next exit attempt can retry. Exit retries
one failed restore before reporting an error. Room unload calls the same exit
path before the lifecycle bridge clears the room.

Pure guard coverage is automated. On 2026-11-09, real-profile checks verified
normal entry/exit state restoration and rejection in unsupported contexts; the
user reported no visible crashes or unexpected behavior, and the relevant
BepInEx log contained no exception or stack trace. The same profile session
then injected an activation failure after partial state application and failed
both automatic restore attempts. The guard retained the snapshot; the user's
subsequent retry restored the game state. The complete BepInEx and Unity logs
were captured for session `137728e8be1c409699a1604d1768652d`; the process exited
normally and no unexpected exception or crash signature was found.

When the editor is resumed without closing its widget, the guard restores the
captured player control, damage, invulnerability, and collision state once.
Further Freeze/Resume toggles leave live gameplay state alone; exit captures
the current player fields before restoring the editor-specific UI and input
state. Automated coverage includes successful resume, repeated resume, exit,
and a failed-resume rollback. Real-profile verification of air movement and an
enemy hit while the editor remains open is pending.
