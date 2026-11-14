# Local automated verification

Run the automated gate from the repository root:

```powershell
pwsh -NoProfile -File scripts/verify.ps1
```

Requires PowerShell 7.2 or newer.

The default run:

- records `git rev-parse HEAD` and requires a clean source tree before and after verification;
- computes the build-profile digest from the committed restore inputs;
- checks production and test source inputs for trailing whitespace and a final LF;
- builds `Blasphemous.EditorMode` in `Release` with `DisableDevelopmentDeployment=true`;
- runs the explicit `tests/Blasphemous.EditorMode.Tests/Blasphemous.EditorMode.Tests.csproj` project;
- rejects a failed test run or zero executed tests; and
- writes `artifacts/automated-evidence.json`.

The gate never deploys to the game directory. To deploy deliberately, run the
normal solution build without `DisableDevelopmentDeployment=true`:

```powershell
dotnet build Blasphemous.EditorMode.sln --configuration Release
```

That remains a separate action outside this gate.

## Locked restore refresh

When a project or package reference intentionally changes, regenerate both lock
files and commit the resulting lock changes:

```powershell
dotnet restore Blasphemous.EditorMode/Blasphemous.EditorMode.csproj --force-evaluate -p:RestoreLockedMode=false
dotnet restore tests/Blasphemous.EditorMode.Tests/Blasphemous.EditorMode.Tests.csproj --force-evaluate -p:RestoreLockedMode=false
```

Do not hand-edit `packages.lock.json`. After committing the refreshed locks, rerun
the gate. Do not reuse evidence produced from earlier lock inputs.

## Build-profile digest

The digest covers these deterministic build inputs:

- `NuGet.Config`
- `Blasphemous.EditorMode/Blasphemous.EditorMode.csproj`
- `Blasphemous.EditorMode/NuGet.Config`
- `Blasphemous.EditorMode/packages.lock.json`
- `tests/Blasphemous.EditorMode.Tests/Blasphemous.EditorMode.Tests.csproj`
- `tests/Blasphemous.EditorMode.Tests/packages.lock.json`

Normalize CRLF and CR line endings to LF. Sort the relative paths with ordinal
comparison. For each path, append the path, a NUL byte, the lowercase SHA-256 of
its normalized UTF-8 bytes, and LF. The final digest is the lowercase SHA-256 of
that UTF-8 canonical text.

## Evidence

The evidence file is generated and ignored by Git. Its canonical shape is:

```json
{
  "sourceSha": "<40 lowercase hex characters>",
  "sourceTreeStatus": "clean",
  "buildDllSha256": "<64 lowercase hex characters>",
  "buildProfileDigest": "<64 lowercase hex characters>",
  "tests": {
    "project": "tests/Blasphemous.EditorMode.Tests/Blasphemous.EditorMode.Tests.csproj",
    "status": "PASS",
    "executed": 81,
    "passed": 81,
    "failed": 0,
    "skipped": 0
  },
  "whitespace": {
    "status": "PASS",
    "checkedFiles": 55,
    "findings": []
  },
  "automatedStatus": "PASS",
  "sdkVersion": "<dotnet --version>",
  "runner": {
    "os": "Windows",
    "osVersion": "<Windows version>",
    "architecture": "<process architecture>",
    "powershellVersion": "<PowerShell version>",
    "machineName": "<runner name>"
  },
  "errors": []
}
```

On failure, `automatedStatus` is `FAIL`, `sourceTreeStatus` identifies a dirty
tree when applicable, `errors` explains the failed gate, and the process exits
non-zero. Test and whitespace sub-statuses identify the failed stage; stages that
did not run remain `NOT_RUN`. Ignored build and evidence files do not dirty the
source tree.

Run the Pester coverage with:

```powershell
pwsh -NoProfile -Command '$config = New-PesterConfiguration; $config.Run.Path = "tests/verification/LocalAutomatedGate.Tests.ps1"; $config.Output.Verbosity = "Detailed"; $config.TestRegistry.Enabled = $false; Invoke-Pester -Configuration $config'
```
