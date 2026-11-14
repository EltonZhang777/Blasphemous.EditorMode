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
  "schemaVersion": 1,
  "evidenceKind": "automated",
  "generatedAt": "<ISO-8601 UTC timestamp>",
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

## Runtime smoke evidence

`smoke` mode never launches the game and never writes the game directory or
GitHub. It records the current source SHA (or an explicit `-SourceSha`), the
deployed `EditorMode.dll` hash, the canonical runtime dependency profile hash,
and the result reported by the caller:

```powershell
pwsh -NoProfile -File scripts/verify.ps1 -Mode smoke `
  -ProfileRoot '<profile>' -SmokeOutcome PASS
```

`-SmokeOutcome` is `PARTIAL` by default. Use `PASS` or `FAIL` only after a real
target-profile launch and gameplay smoke was observed; file presence alone is
not runtime evidence. Missing or unreadable required dependencies force
`runtimeStatus` and `outcome` to `FAIL`. The evidence is written to
`artifacts/smoke-evidence.json`. `-GeneratedAt` can preserve the observed run
timestamp in ISO-8601 form.

The profile hash includes `ModdingAPI.dll`, `LevelFramework.dll`,
`UIFramework.dll`, and optional `LevelFrameworkExtended.dll` in that fixed
order. Entry paths are profile-relative, hashes are uppercase SHA-256, and the
canonical text is hashed with SHA-256. A missing optional dependency is recorded
as `absent`. Different binary bytes therefore produce a different profile hash.

## Evidence publication

Publication defaults to a read-only dry run. It reads the PR and target comments,
writes `artifacts/publication-preview.json`, and makes no GitHub writes:

```powershell
pwsh -NoProfile -File scripts/verify.ps1 -Mode publish `
  -EvidenceKind dev-automated -PrNumber 42 -Repository owner/repo
```

Add `-Publish` to upsert the marker comment. `pr-head-automated` targets the PR
and requires its current head to equal the evidence source SHA.
`dev-automated` and `runtime-smoke` target the single evidence issue declared in
the PR body as `Evidence issue: #N`; missing, multiple, malformed, or issue `#1`
declarations are rejected. The publisher never edits a PR or issue body.

Each evidence kind has one marker comment. A newer run updates that comment in
place; an equal-or-older run is rejected. The comment contains one JSON block
between `codex-evidence-json:start` and `codex-evidence-json:end`. Automated and
runtime status are reported separately. Overall `PASS` requires both to be
current and `PASS`; missing, stale, or source-SHA-mismatched runtime evidence is
`PARTIAL`; an observed runtime failure is `FAIL`.

The marker uses this shape; the embedded JSON is the single-line publication
record:

```markdown
<!-- codex-evidence:runtime-smoke -->
## Verification evidence: runtime-smoke

- Source SHA: `<40 lowercase hex characters>`
- Automated: `PASS|FAIL|PARTIAL`
- Runtime: `PASS|FAIL|PARTIAL`
- Overall: `PASS|FAIL|PARTIAL`

<!-- codex-evidence-json:start -->
{"schemaVersion":1,"evidenceKind":"runtime-smoke","sourceSha":"...","generatedAt":"...","automatedStatus":"...","runtimeStatus":"...","overallStatus":"..."}
<!-- codex-evidence-json:end -->
```

Run the Pester coverage with:

```powershell
pwsh -NoProfile -Command '$config = New-PesterConfiguration; $config.Run.Path = "tests/verification"; $config.Output.Verbosity = "Detailed"; $config.TestRegistry.Enabled = $false; Invoke-Pester -Configuration $config'
```
