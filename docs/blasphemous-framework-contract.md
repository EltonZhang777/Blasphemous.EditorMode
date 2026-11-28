# Blasphemous Level Framework Contract

Status: static contract and runtime creator inventory verified against the target profile. Runtime scene loading and editor interactions are covered by separate Editor Mode smoke evidence.

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

## Verified base creator metadata reference

| Type | Display name | Category | Source scene | Source path | Transform | Properties |
| --- | --- | --- | --- | --- | --- | --- |
| `bell-iron` | Iron Bell | Traps | `D03Z02S06_LOGIC` | `TRAPS/TRAP_SHOCK_CHAIN_REACTION` | position, rotation, scale | none |

The creator is registered by the base Framework `OnRegisterServices` implementation with `SceneLoader` and `NoModifier("Iron bell")`. `BaseModifier` applies the position, rotation, scale, and active state.

The table is a verified reference used by existing tests. It is not catalog membership data: the Content Catalog now uses the runtime registry key as its identity and display fallback, with `Registered`, `Level Framework`, and `Not declared` filling missing metadata. Loader and modifier availability is recorded from the runtime registration.

## Runtime creator inventory boundary

`RuntimeCreatorInventoryReader` reads the Framework's internal creator registry through guarded reflection and builds the complete runtime inventory. Registry, enumeration, and value-access failures become diagnostics; exact registry keys are preserved; and the inventory does not require `LevelFrameworkExtended`.

`RuntimeCreatorRegistry.TryGet` resolves Preview and Place targets with the same exact key. A read or lookup failure leaves the rest of the editor usable and is surfaced through the existing report and log surfaces.

The membership decision is recorded in [ADR 0007](adr/0007-runtime-creator-registry-owns-catalog-membership.md).
