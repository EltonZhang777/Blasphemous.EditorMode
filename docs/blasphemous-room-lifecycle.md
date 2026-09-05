# Blasphemous Room Lifecycle

Status: callback mapping and pure lifecycle behavior are implemented. In-game callback ordering and cleanup remain pending because this goal skips in-game tests.

## Runtime facts

The decompiled target `Framework.Managers.LevelManager` exposes `OnLevelPreLoaded` and `OnLevelLoaded` with `Level` arguments. `InitializeLevel` sets the active scene to the current level's `LOGIC` scene, then invokes `OnLevelPreLoaded`; the normal load path invokes `OnLevelLoaded` after player initialization and input reset.

The modding API maps those callbacks to `BlasMod.OnLevelPreloaded(string, string)`, `OnLevelLoaded(string, string)`, and `OnLevelUnloaded(string, string)`. The bridge uses the Framework level key and the active Unity scene name to build a Room Identity.

## Room Identity

For a room such as `D03Z02S06`, the expected loaded scene is `D03Z02S06_LOGIC`. The identity stores:

- Framework level key: `D03Z02S06`
- target scene suffix: `_LOGIC`
- active Unity scene: `D03Z02S06_LOGIC`
- Framework export file key: `D03Z02S06`

MainMenu, unrelated active scenes, unsupported scene suffixes, and missing values do not create a Room Overlay.

## Bridge behavior

- `OnLevelPreloaded` clears the current room before a transition and enters `Transitioning`.
- `OnLevelLoaded` creates one room identity after the scene is ready.
- Repeated loaded callbacks for the same identity do not invoke the room-loaded callback twice.
- Changing rooms invokes the room-unloaded callback once, allowing the runtime placement layer to clean Architect-owned objects.
- Invalid loaded data enters `Faulted` without creating a room.

The bridge does not write the Original Save and does not treat `OnLevelPreloaded` as a real-time editing API.
