# Rebind the room context before player spawn

The game's player effects cache room-owned services during spawn, while a room transition replaces those services. Editor Mode therefore checks the active logic scene during the ModdingAPI preloaded callback and updates the current room reference only when it is stale; this repairs the context before player and enemy components cache it, instead of swallowing their later null-reference failures.
