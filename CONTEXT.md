# Architect Level Editing

This context defines the vocabulary for porting Architect's runtime level-editing experience from Silksong to Blasphemous. It distinguishes authored level content from the game state in which that content is applied.

## Authoring

**Editor Mode**:
A controlled in-game state in which a user inspects and changes level content rather than progressing through normal gameplay.
_Avoid_: Debug Mode, which describes access control rather than the editing experience.

**Level**:
A reusable authored arrangement of content and room changes, initially applied to the current room and later extensible to standalone custom levels.
_Avoid_: Save Slot, which is player progression data.

**Layout**:
The authored content of a level: placed objects, their editable properties, and any supported room-level changes.
_Avoid_: Scene, which is the game's runtime representation of a room.

**Placement**:
One instance of a placeable object within a layout, including its position and other user-editable state.
_Avoid_: Spawn, which describes runtime creation rather than authored content.

**Placeable Object**:
A supported game object definition that can be selected, previewed, and instantiated in a layout.
_Avoid_: Asset, which may refer to any game or mod resource, including resources that are not editable content.

## Runtime Boundaries

**Room Overlay**:
The application of a layout to an existing game room without redefining the game's packaged room as a new authored level.
_Avoid_: Scene Mutation, which implies permanent modification of packaged game resources.

**Standalone Level**:
A layout treated as an independently authored play space rather than an overlay of one existing room.
_Avoid_: Map, unless the scope includes room connectivity and world traversal.

**Original Save**:
The game's player progression and save-slot state, which remains separate from authored level content.
_Avoid_: Level Data, because level data belongs to the editor's content model.

**Level Framework**:
The external Blasphemous service that defines the supported level lifecycle and persistence contract for authored level content.
_Avoid_: Editor, because the framework stores/applies level content while the editor creates and changes it.

**Level Export**:
A local JSON artifact representing authored level content in the format accepted by the Level Framework.
_Avoid_: Original Save, because exporting a level must not rewrite player progression.

## Content and Distribution

**Content Catalog**:
The set of placeable object definitions exposed to the editor for a target game and mod runtime.
_Avoid_: Object Registry when discussing the user-facing set of available content.

**Reusable Content**:
User-authored level elements that can be copied, shared, or placed in more than one layout.
_Avoid_: Workshop, which is a distribution surface rather than the content itself.

**Extension Mod**:
An attached mod that adds supported game-object creators or other capabilities around the Level Framework without changing the editor's core vocabulary.
_Avoid_: Framework Fork, which would mean changing the external framework itself.
