# Blasphemous Target Profile

Status: observed from the configured local modding profile and verified project/source metadata. The deployed profile is authoritative for runtime compatibility; source repository versions are evidence only.

## Version matrix

| Component | Observed target | Evidence / policy |
| --- | --- | --- |
| Unity | 2017.4.40f1 baseline | Blasphemous ModdingAPI project and modding guidance use Unity 2017.4.40; the deployed managed Unity assemblies report `0.0.0.0`, so the game/runtime baseline remains the authority. |
| Blasphemous.ModdingAPI | 3.0.1.0 | Deployed `ModdingAPI.dll`; the mod build and hard dependency use minimum `3.0.1`. |
| Blasphemous.Framework.Levels | 0.2.0.0 | Deployed `LevelFramework.dll`; the mod build and hard dependency use minimum `0.2.0`. |
| Blasphemous.Framework.UI | 0.2.0.0 | Deployed `UIFramework.dll`; the mod build and hard dependency use minimum `0.2.0`. |
| Blasphemous.GameLibs | 4.0.67 | Compile-time package reference; it is not a standalone plugin in the target profile. |
| LevelFrameworkExtended | 0.1.0.0 | Present in the profile but optional; it is not part of the dependency gate. |
| Blasphemous game assemblies | `0.0.0.0` assembly metadata | Not used as a meaningful semantic version; compatibility is tied to the target profile and verified APIs. |

## Dependency policy

- BepInEx soft dependencies advertise the preferred load order without suppressing the bootstrap plugin before it can report a diagnostic.
- The runtime gate checks the loaded plugin registry before constructing the `EditorMode` service and reports missing or too-old dependencies through the BepInEx logger and `Main.DependencyStatus`.
- A newer compatible dependency version satisfies the minimum-version requirement; an older version is rejected.
- The local Framework source project currently declares older metadata than the deployed profile. The target mod follows the deployed profile/package matrix and does not modify the external Framework project.

## Verification boundary

The profile is available locally, but a successful build and pure logic tests do not prove the game smoke criteria. Runtime behavior remains pending until the target game profile is launched and the dependency gate log is inspected.
