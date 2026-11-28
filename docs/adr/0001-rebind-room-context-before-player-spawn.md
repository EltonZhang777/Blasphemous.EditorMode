# Rebind the room context before player spawn

Blasphemous.ModdingAPI routes `OnLevelPreloaded` from the game's `LevelManager.OnLevelPreLoaded`, before enemy and player spawning. On the first room, the active initializer's `LevelEffectsStore` property may not be initialized at this boundary even though the component is present. The game assigns that property in `LevelInitializer.Awake`, before effect components read `Core.Logic.CurrentLevelConfig.LevelEffectsStore` in `Start`.

## Structural binding

For a non-MainMenu target, structural binding requires an active and enabled `LevelInitializer` whose hierarchy contains a `LevelEffectsStore` component. When that evidence is present, the context may be rebound to the initializer even while the initializer's store property is uninitialized. A rebind changes context ownership only; it is not a readiness claim.

MainMenu is not-applicable. A target with an invalid level, unavailable Logic, invalid active scene, missing or inactive initializer, or missing store component fails closed and preserves a concrete reason. An unavailable Room Identity fails the readiness decision, but it is not part of structural evidence: a candidate that otherwise satisfies structural binding may still be rebound and is recorded as failed, not ready.

## Readiness

The context is ready only when `Core.Logic.CurrentLevelConfig` is the current room's initializer and that initializer's store property is initialized. A present store component with an uninitialized property is observed but not ready, whether or not a structural rebind occurred.

The Unity adapter records the decision at `OnLevelPreloaded` and again at `OnLevelLoaded`. Each structured record carries the callback stage, level, active scene, derivable Room Identity, initializer and store-component state, store-value state, context before and after any rebind, the decision and reason, and frame correlation. The later record is a separate observation and does not retroactively make the preload-time context ready.

A runtime behavior change requires first capturing a fresh-process, normal-entry first-room failure with an NRE stack or a correlated failed, observed, or rebound startup record. Without that evidence, lifecycle timing must not be changed.
