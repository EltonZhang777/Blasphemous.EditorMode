# Rebind the room context before player spawn

The game's player effects cache room-owned services during spawn, while a room transition replaces those services. The ModdingAPI preloaded callback can observe a stale current-room reference, so Editor Mode may refresh it only from the active scene's `LevelInitializer` after its `LevelEffectsStore` is available; if that service is not ready, the game remains responsible for publishing the initialized context. Do not suppress later null-reference failures.
