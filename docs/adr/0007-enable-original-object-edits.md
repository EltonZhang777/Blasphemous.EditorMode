# Enable validated Original Object edits

Status: Accepted.

ADR-0003 kept Original Objects read-only because they lacked a validated identity and an editor-owned mutation boundary. The pre-mod baseline now supplies positive provenance, scene/path resolution is checked against the same runtime GameObject, and `LevelEditSession` owns mutable `modifications` and `deletions`. A selected locatable Original Object can therefore receive a transform modification even though it is not an Editable Overlay Placement.

A new transform-only operation uses the reserved type `editor-mode-empty`, a deterministic session-unique id, an empty condition, and an empty properties array. Imported type, id, condition, properties, and all transform fields not explicitly edited are preserved. There is at most one resolved operation for a scene/path; Apply Modify upserts that identity and replaces a deletion for it. Level Framework remains the persistence contract: the editor previews the modification live, but only an explicit Level Export writes canonical JSON.

Live application verifies that the type-specific modifier is available before changing the object. It then applies `BaseModifier` and only afterward the type modifier, so missing or throwing modifiers cannot leave a transform-only partial preview. Failures are itemized and the captured transform, active state, and name are restored without committing session state or rewriting the source file. `editor-mode-empty` is a temporary Editor Mode creator whose modifier performs no name or component mutation. A portable `empty` creator in Level Framework Extended is the intended long-term replacement; migrations to that portable type remain a separate change.

Original Object history is independent from Editable Overlay Placement history. Undo and redo restore both the session arrays and the recoverable live state. A preview persists while Editor Mode is closed and reopened, and ends at room unload, Reset, operation replacement or removal, Room Overlay replacement, or session teardown. Reset restores the entire session to the imported snapshot and clears both page histories.
