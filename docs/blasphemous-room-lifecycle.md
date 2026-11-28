# Blasphemous Room Lifecycle

Status: callback mapping and pure lifecycle behavior are implemented. The target profile now confirms room entry and bell-iron placement/import after creator preload; repeated callback ordering, transitions, and cleanup remain pending.

## Domain vocabulary

Use the root [GLOSSARY.md](../GLOSSARY.md) as the authoritative vocabulary. Relevant decisions are recorded in [ADR 0002](adr/0002-level-edit-session-owns-authored-room-state.md), [ADR 0003](adr/0003-preserve-original-objects-as-read-only.md), [ADR 0004](adr/0004-preserve-unmodelable-additions.md), and [ADR 0005](adr/0005-name-editable-overlay-placements-by-edit-authority.md).

## Runtime facts

The decompiled target `Framework.Managers.LevelManager` exposes `OnLevelPreLoaded` and `OnLevelLoaded` with `Level` arguments. `InitializeLevel` sets the active scene to the current level's `LOGIC` scene, then invokes `OnLevelPreLoaded`; the normal load path invokes `OnLevelLoaded` after player initialization and input reset.

The modding API maps those callbacks to `BlasMod.OnLevelPreloaded(string, string)`, `OnLevelLoaded(string, string)`, and `OnLevelUnloaded(string, string)`. The bridge uses the Framework level key and the active Unity scene name to build a Room Identity.

## Room identity values

The authoritative definition is in [GLOSSARY.md](../GLOSSARY.md). For a room such as `D03Z02S06`, the expected loaded scene is `D03Z02S06_LOGIC`. The lifecycle bridge records:

- Framework level key: `D03Z02S06`
- target scene suffix: `_LOGIC`
- active Unity scene: `D03Z02S06_LOGIC`
- Framework export file key: `D03Z02S06`

MainMenu, unrelated active scenes, unsupported scene suffixes, and missing values do not create a Room Overlay.

## Bridge behavior

- `OnLevelPreloaded` clears the current room before a transition and enters `Transitioning`.
- `OnLevelLoaded` creates one room identity after the scene is ready.
- Repeated loaded callbacks for the same identity do not invoke the room-loaded callback twice.
- Changing rooms invokes the room-unloaded callback once, allowing the runtime placement layer to clean Editable Overlay Placements.
- Invalid loaded data enters `Faulted` without creating a room.

The bridge does not write the Original Save and does not treat `OnLevelPreloaded` as a real-time editing API.

## Creator source preload

Start creator preload only after a validated Room Identity exists. A prior attempt from MainMenu logged `[Error :Editor Mode] Creator source preload failed for bell-iron: Scene to unload is invalid`; the preload is now gated on `CurrentRoom` and begins after the room-load callback. The latest target-profile smoke loaded the room and placed/imported bell-iron without reproducing the error. This verifies the bell-iron path only; other creators and broader transition/cleanup behavior still need coverage.
