# Blasphemous Editor Mode

Terms for editing level content in the currently loaded Blasphemous room.

## Room lifecycle

**Room Runtime Context**:
The active room's level-bound state and services used by gameplay and Editor Mode. It is valid only while those services belong to and are initialized for that room; the previous room's context must not outlive a transition.
_Avoid_: Level context

**Room Identity**:
The stable identity of the room to which a Room Overlay and its Level Export belong: the Level Framework key, target scene suffix, active Unity scene, and export file key.
_Avoid_: Scene name, because one room has multiple role-specific scenes

**Room Overlay**:
The application of a Layout to an existing game room without redefining the packaged room as a new authored level.
_Avoid_: Scene Mutation, which implies permanent modification of packaged game resources

**Preview**:
An isolated, non-gameplay inspection instance of a Placeable Object shown before placement. A Preview is neither a Placement nor part of Room Overlay content.
_Avoid_: Ghost object, placement preview

## Authoring

**Editor Mode**:
A controlled in-game state in which a user inspects and changes level content rather than progressing through normal gameplay.
_Avoid_: Debug Mode, which describes access control rather than the editing experience

**Level**:
A reusable authored arrangement of content and room changes, initially applied to the current room and later extensible to standalone custom levels.
_Avoid_: Save Slot, which is player progression data

**Layout**:
The authored content of a level: placed objects, their editable properties, and any supported room-level changes.
_Avoid_: Scene, which is the game's runtime representation of a room

**Placement**:
One instance of a Placeable Object within a Layout, including its position and other user-editable state.
_Avoid_: Spawn, which describes runtime creation rather than authored content

**Placeable Object**:
A supported game object definition that can be selected, previewed, and instantiated in a Layout.
_Avoid_: Asset, which may refer to any game or mod resource, including resources that are not editable content

**Editable Overlay Placement**:
Any validated addition admitted into the current Level Edit Session, whether newly authored or imported. The term denotes editable authority, not the object's provenance.
_Avoid_: Architect-owned, owned object, author-created addition

**Preserved Addition**:
A Level Framework addition that the current editor profile cannot fully represent or validate. It is retained as its original JSON value for round-trip, is not a Placement, and is not editable.
_Avoid_: Uneditable addition, opaque addition

## Level editing session

**Level Edit Session**:
The per-room domain state for one Room Overlay, containing its Editable Overlay Placements and edit history.
_Avoid_: Session cache, editing scene

**ApplyReport**:
The structured outcome of an editing, import, or export operation, including applied changes, warnings, and errors.
_Avoid_: Import log, status blob

## Runtime boundaries

**Standalone Level**:
A Layout treated as an independently authored play space rather than an overlay of one existing room.
_Avoid_: Map, unless the scope includes room connectivity and world traversal

**Original Object**:
A game object already supplied by the packaged room, before editor additions are applied. The current editor phase does not expose editing of Original Objects.
_Avoid_: Original, when referring to the internal PlacementOwnership member, vanilla object

**Original Object Edit**:
A Level Framework modification or deletion that targets an Original Object. The editor currently retains Original Object Edits for round-trip but does not expose them as editable operations.
_Avoid_: Original modification, deletion record

**Original Save**:
The game's player progression and save-slot state, which remains separate from authored level content.
_Avoid_: Level Data, because level data belongs to the editor's content model

**Level Framework**:
The external Blasphemous service that defines the supported level lifecycle and persistence contract for authored level content.
_Avoid_: Editor, because the framework stores and applies level content while the editor creates and changes it

**Level Export**:
A local JSON artifact representing authored level content in the format accepted by the Level Framework.
_Avoid_: Original Save, because exporting a level must not rewrite player progression

## Content and distribution

**Content Catalog**:
The set of Placeable Object definitions exposed to the editor for a target game and mod runtime.
_Avoid_: Object Registry, when discussing the user-facing set of available content

**Reusable Content**:
User-authored level elements that can be copied, shared, or placed in more than one Layout.
_Avoid_: Workshop, which is a distribution surface rather than the content itself

**Extension Mod**:
An attached mod that adds supported game-object creators or other capabilities around the Level Framework without changing the editor's core vocabulary.
_Avoid_: Framework Fork, which would mean changing the external framework itself
