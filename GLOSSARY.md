# Blasphemous Editor Mode

Terms for editing level content in the currently loaded Blasphemous room.

## Room lifecycle

**Room Runtime Context**:
The active room's level-bound state and services used by gameplay and Editor Mode. It is valid only while those services belong to and are initialized for that room; the previous room's context must not outlive a transition.
_Avoid_: Level context
