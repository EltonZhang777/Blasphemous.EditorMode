# Level Edit Session owns authored room state

The Level Edit Session is the only mutable authority for committed Room Overlay authored state; runtime Unity GameObjects are projections of that state. Commands change the session, and the runtime projection is reconciled from it, so undo/redo, export, and room reload do not depend on scene-object identity. Preview is an isolated inspection instance and is neither a Placement nor authored room state.
