# Adopt the local template baseline for tests and deployment

Status: Accepted; code migration pending.

`Blasphemous.EditorMode.Tests` is an adjacent `net48` xUnit project that directly references the real `net35` Mod assembly. The Mod grants `InternalsVisibleTo` only to the `Blasphemous.EditorMode.Tests` assembly name, so tests exercise the shipped assembly identity without linking a divergent copy of production source or making production types public solely for testing. Replacing the test assembly name, signing policy, or internal-access mechanism supersedes this part of the decision.

The `Development` target keeps the repository-root `publish/EditorMode.zip` path and emits `plugins/EditorMode.dll` plus all files in the `data/` directory, without test projects or test assemblies. A default or `DeployToGame=false` build only packages and does not write to a game directory. Deployment requires both `DeployToGame=true` and an existing directory in `DevFolder`; an empty, missing, or non-directory target fails with an actionable error, and there is no Steam default path. `DisableDevelopmentDeployment` is retired. Replacing the package path, target semantics, or the single explicit deployment switch supersedes this part of the decision.
