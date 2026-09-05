# Blasphemous Level Framework Contract

Status: static contract and creator metadata verified against the target profile. Runtime scene loading remains pending because this goal skips in-game tests.

## Evidence

- Target assembly: `C:\Users\28090\Desktop\Games\Games\versions_of_blasphemous_1\渎神 mod测试  开发中mod\Modding\plugins\LevelFramework.dll`
- Observed assembly version: `0.2.0.0`
- Reference source: `C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels\Blasphemous.Framework.Levels\ObjectData.cs`
- Reference source: `C:\Users\28090\Documents\GitHub\Blasphemous.Framework.Levels\Blasphemous.Framework.Levels\LevelFramework.cs`
- Runtime profile contains `LevelFrameworkExtended.dll`, but the editor does not require or reference it.

## Canonical JSON

The base Framework reads three top-level arrays. Field names are lowercase because they match `JsonProperty` names in `ObjectData` and `LevelEdit`.

```json
{
  "additions": [],
  "modifications": [],
  "deletions": []
}
```

`additions` require `type`, `id`, `position`, `rotation`, `scale`, `condition`, and `properties`.

`modifications` require those fields plus `scene` and `path`.

`deletions` require `scene` and `path` only. The base Framework uses those values to find the original object.

The Framework defaults `position` and `rotation` to `(0, 0, 0)`, `scale` to `(1, 1, 1)`, `condition` to an empty string, and `properties` to an empty array. Editor-only metadata is not part of this document.

## Verified base creator metadata

| Type | Display name | Category | Source scene | Source path | Transform | Properties |
| --- | --- | --- | --- | --- | --- | --- |
| `bell-iron` | Iron Bell | Traps | `D03Z02S06_LOGIC` | `TRAPS/TRAP_SHOCK_CHAIN_REACTION` | position, rotation, scale | none |

The creator is registered by the base Framework `OnRegisterServices` implementation with `SceneLoader` and `NoModifier("Iron bell")`. `BaseModifier` applies the position, rotation, scale, and active state.

## Probe boundary

`RuntimeCreatorProbe` reads the Framework's internal creator registry only for the curated metadata check. It catches reflection and registry failures, then returns a diagnostic. It does not make `LevelFrameworkExtended` a dependency.

The current goal intentionally does not claim that `bell-iron` loaded a scene or produced an isolated runtime object. That requires an in-game test in the configured profile.
