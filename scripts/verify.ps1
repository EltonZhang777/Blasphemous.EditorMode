#Requires -Version 7.2

[CmdletBinding()]
param(
    [ValidateSet('automated', 'smoke', 'publish', 'transport', 'publish-pr')]
    [string] $Mode = 'automated',

    [string] $RepoRoot,

    [string] $ProfileRoot,

    [ValidateSet('PASS', 'FAIL', 'PARTIAL')]
    [string] $SmokeOutcome = 'PARTIAL',

    [ValidateSet('pr-head-automated', 'dev-automated', 'runtime-smoke')]
    [string] $EvidenceKind = 'pr-head-automated',

    [int] $PrNumber = 0,

    [string] $Repository,

    [string] $SourceSha,

    [string] $GeneratedAt,

    [string] $TransportOutputPath,

    [string] $TransportPath,

    [int] $TransportMaxBytes = 262144,

    [int] $FreshnessMinutes = 30,

    [string] $CheckContext = 'Trusted verification evidence',

    [long] $WorkflowRunId = 0,

    [int] $WorkflowRunAttempt = 0,

    [string] $WorkflowName = 'PR verification',

    [string] $WorkflowPath = '.github/workflows/verify-pr.yml',

    [string] $WorkflowEvent = 'pull_request_target',

    [ValidateSet('success', 'failure')]
    [string] $WorkflowConclusion,

    [switch] $Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
}
else {
    [IO.Path]::GetFullPath($RepoRoot)
}
$productionProject = Join-Path $repoRoot 'Blasphemous.EditorMode\Blasphemous.EditorMode.csproj'
$testProject = Join-Path $repoRoot 'tests\Blasphemous.EditorMode.Tests\Blasphemous.EditorMode.Tests.csproj'
$testProjectRelative = 'tests/Blasphemous.EditorMode.Tests/Blasphemous.EditorMode.Tests.csproj'
$buildDll = Join-Path $repoRoot 'Blasphemous.EditorMode\bin\Release\EditorMode.dll'
$evidencePath = Join-Path $repoRoot 'artifacts\automated-evidence.json'
$smokeEvidencePath = Join-Path $repoRoot 'artifacts\smoke-evidence.json'
$publicationPreviewPath = Join-Path $repoRoot 'artifacts\publication-preview.json'
$publicationResultPath = Join-Path $repoRoot 'artifacts\publication-result.json'
$transportOutputPath = if ([string]::IsNullOrWhiteSpace($TransportOutputPath)) {
    Join-Path $repoRoot 'artifacts\pr-verification-transport.json'
}
else {
    [IO.Path]::GetFullPath($TransportOutputPath)
}
$transportInputPath = if ([string]::IsNullOrWhiteSpace($TransportPath)) {
    $null
}
else {
    [IO.Path]::GetFullPath($TransportPath)
}
$checkRunRequestPath = Join-Path $repoRoot 'artifacts\check-run-request.json'
$testResultsDirectory = Join-Path $repoRoot 'artifacts\test-results'

$runtimeDependencies = @(
    [pscustomobject]@{ logicalName = 'Blasphemous.ModdingAPI'; file = 'ModdingAPI.dll'; required = $true }
    [pscustomobject]@{ logicalName = 'Blasphemous.Framework.Levels'; file = 'LevelFramework.dll'; required = $true }
    [pscustomobject]@{ logicalName = 'Blasphemous.Framework.UI'; file = 'UIFramework.dll'; required = $true }
    [pscustomobject]@{ logicalName = 'LevelFrameworkExtended'; file = 'LevelFrameworkExtended.dll'; required = $false }
)

$buildProfileFiles = @(
    'NuGet.Config'
    'Blasphemous.EditorMode/Blasphemous.EditorMode.csproj'
    'Blasphemous.EditorMode/NuGet.Config'
    'Blasphemous.EditorMode/packages.lock.json'
    'tests/Blasphemous.EditorMode.Tests/Blasphemous.EditorMode.Tests.csproj'
    'tests/Blasphemous.EditorMode.Tests/packages.lock.json'
)

$whitespaceExtensions = @(
    '.config'
    '.cs'
    '.csproj'
    '.json'
    '.props'
    '.ps1'
    '.psd1'
    '.psm1'
    '.targets'
)

$script:evidence = [ordered]@{
    schemaVersion = 1
    evidenceKind = 'automated'
    generatedAt = $null
    sourceSha = $null
    sourceTreeStatus = $null
    buildDllSha256 = $null
    buildProfileDigest = $null
    tests = [ordered]@{
        project = $testProjectRelative
        status = 'NOT_RUN'
        executed = 0
        passed = 0
        failed = 0
        skipped = 0
    }
    whitespace = [ordered]@{
        status = 'NOT_RUN'
        checkedFiles = 0
        findings = @()
    }
    automatedStatus = 'FAIL'
    sdkVersion = $null
    runner = [ordered]@{
        os = if ($IsWindows) { 'Windows' } else { 'NonWindows' }
        osVersion = [Environment]::OSVersion.VersionString
        architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        machineName = [Environment]::MachineName
    }
    errors = @()
}

$script:errorMessages = [Collections.Generic.List[string]]::new()

function Write-Evidence {
    $script:evidence.errors = @($script:errorMessages)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidencePath) | Out-Null
    $json = $script:evidence | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($evidencePath, $json + "`n", [Text.UTF8Encoding]::new($false))
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string] $Command,
        [string[]] $Arguments = @()
    )

    $output = & $Command @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output)
    }
}

function Get-SourceSha {
    $result = Invoke-NativeCommand -Command 'git' -Arguments @('rev-parse', 'HEAD')
    if ($result.ExitCode -ne 0) {
        throw "git rev-parse HEAD failed with exit code $($result.ExitCode)."
    }

    $sha = (($result.Output | ForEach-Object { $_.ToString() }) -join '').Trim()
    if ($sha -notmatch '^[0-9a-f]{40}$') {
        throw "git rev-parse HEAD returned a malformed SHA: '$sha'."
    }
    return $sha
}

function Assert-CleanSourceTree {
    param(
        [Parameter(Mandatory)]
        [string] $Phase
    )

    $result = Invoke-NativeCommand -Command 'git' -Arguments @(
        'status'
        '--porcelain'
        '--untracked-files=all'
    )
    if ($result.ExitCode -ne 0) {
        throw "git status failed with exit code $($result.ExitCode)."
    }

    $lines = @($result.Output | ForEach-Object { $_.ToString() } | Where-Object { $_ -ne '' })
    $script:evidence.sourceTreeStatus = if ($lines.Count -eq 0) { 'clean' } else { 'dirty' }
    if ($lines.Count -ne 0) {
        $preview = @($lines | Select-Object -First 10) -join '; '
        $suffix = if ($lines.Count -gt 10) { '; ...' } else { '' }
        throw "Source tree is dirty $Phase ($($lines.Count) path(s)): $preview$suffix"
    }
}

function Get-BuildProfileDigest {
    $sortedFiles = [string[]]$buildProfileFiles.Clone()
    [Array]::Sort($sortedFiles, [StringComparer]::Ordinal)
    $canonical = [Text.StringBuilder]::new()

    foreach ($relativePath in $sortedFiles) {
        $fullPath = Join-Path $repoRoot ($relativePath -replace '/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Build profile input is missing: $relativePath"
        }

        $text = [IO.File]::ReadAllText($fullPath).Replace("`r`n", "`n").Replace("`r", "`n")
        $hash = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))
        ).ToLowerInvariant()
        [void]$canonical.Append($relativePath).Append("`0").Append($hash).Append("`n")
    }

    $digest = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical.ToString()))
    ).ToLowerInvariant()
    return $digest
}

function Test-WhitespaceInputs {
    $roots = @(
        (Join-Path $repoRoot 'Blasphemous.EditorMode')
        (Join-Path $repoRoot 'tests')
    )
    $candidates = [Collections.Generic.List[IO.FileInfo]]::new()

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            throw "Whitespace input root is missing: $root"
        }

        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
            $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
            $segments = $relative -split '[\\/]'
            if ($segments.Count -gt 1 -and ($segments[0..($segments.Count - 2)] | Where-Object { $_ -in @('bin', 'obj', 'tmp', '.temp') })) {
                continue
            }
            $candidates.Add($file)
        }
    }

    $rootNuGetConfig = Join-Path $repoRoot 'NuGet.Config'
    if (Test-Path -LiteralPath $rootNuGetConfig -PathType Leaf) {
        $candidates.Add((Get-Item -LiteralPath $rootNuGetConfig))
    }

    $files = @(
        $candidates |
            Where-Object { $whitespaceExtensions -contains $_.Extension.ToLowerInvariant() } |
            Sort-Object FullName
    )
    if ($files.Count -eq 0) {
        throw 'Whitespace check found no production or test inputs.'
    }

    $findings = [Collections.Generic.List[object]]::new()
    foreach ($file in $files) {
        $relativePath = [IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        if ($bytes.Length -eq 0 -or $bytes[-1] -ne 10) {
            $findings.Add([ordered]@{
                path = $relativePath
                kind = 'final-newline'
            })
        }

        $lineNumber = 0
        foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
            $lineNumber++
            if ($line -match '[ \t]+$') {
                $findings.Add([ordered]@{
                    path = $relativePath
                    kind = 'trailing-whitespace'
                    line = $lineNumber
                })
            }
        }
    }

    $script:evidence.whitespace = [ordered]@{
        status = if ($findings.Count -eq 0) { 'PASS' } else { 'FAIL' }
        checkedFiles = $files.Count
        findings = @($findings)
    }

    if ($findings.Count -ne 0) {
        throw "Whitespace check failed with $($findings.Count) finding(s)."
    }
}

function Get-TrxCounter {
    param(
        [Parameter(Mandatory)]
        [System.Xml.XmlElement] $Counters,
        [Parameter(Mandatory)]
        [string] $Name,
        [bool] $Required = $true
    )

    $attribute = $Counters.GetAttributeNode($Name)
    if ($null -eq $attribute) {
        if ($Required) {
            throw "Test result counter '$Name' is missing."
        }
        return 0
    }

    $value = 0
    if (-not [int]::TryParse($attribute.Value, [ref] $value) -or $value -lt 0) {
        throw "Test result counter '$Name' is malformed: '$($attribute.Value)'."
    }
    return $value
}

function Read-TestCounts {
    param(
        [Parameter(Mandatory)]
        [string] $TrxPath
    )

    if (-not (Test-Path -LiteralPath $TrxPath -PathType Leaf)) {
        throw "Test result file was not produced: $TrxPath"
    }

    try {
        [xml] $trx = [IO.File]::ReadAllText($TrxPath)
    }
    catch {
        throw "Test result file is malformed: $TrxPath"
    }

    $summaries = @($trx.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']"))
    $allSummaries = @($trx.SelectNodes("//*[local-name()='ResultSummary']"))
    if ($summaries.Count -ne 1 -or $allSummaries.Count -ne 1) {
        throw "Test result must contain exactly one summary; found $($allSummaries.Count)."
    }
    $summary = $summaries[0]
    $outcome = $summary.GetAttribute('outcome')
    if ($outcome -ne 'Completed') {
        throw "Test result summary outcome is '$outcome', expected 'Completed'."
    }

    $counterNodes = @($trx.SelectNodes("/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']"))
    $allCounterNodes = @($trx.SelectNodes("//*[local-name()='Counters']"))
    if ($counterNodes.Count -ne 1 -or $allCounterNodes.Count -ne 1) {
        throw "Test result must contain exactly one counters element; found $($allCounterNodes.Count)."
    }
    $counters = $counterNodes[0]

    $total = Get-TrxCounter -Counters $counters -Name 'total'
    $executed = Get-TrxCounter -Counters $counters -Name 'executed'
    $passed = Get-TrxCounter -Counters $counters -Name 'passed'
    $failed = Get-TrxCounter -Counters $counters -Name 'failed'
    $reportedSkipped = Get-TrxCounter -Counters $counters -Name 'notExecuted'
    $reportedSkipped += Get-TrxCounter -Counters $counters -Name 'notRunnable'

    $rejectedCounters = [ordered]@{
        error = Get-TrxCounter -Counters $counters -Name 'error'
        timeout = Get-TrxCounter -Counters $counters -Name 'timeout'
        aborted = Get-TrxCounter -Counters $counters -Name 'aborted'
        inconclusive = Get-TrxCounter -Counters $counters -Name 'inconclusive'
        passedButRunAborted = Get-TrxCounter -Counters $counters -Name 'passedButRunAborted'
        pending = Get-TrxCounter -Counters $counters -Name 'pending'
        warning = Get-TrxCounter -Counters $counters -Name 'warning'
        disconnected = Get-TrxCounter -Counters $counters -Name 'disconnected'
        completed = Get-TrxCounter -Counters $counters -Name 'completed'
        inProgress = Get-TrxCounter -Counters $counters -Name 'inProgress'
    }
    foreach ($name in $rejectedCounters.Keys) {
        if ($rejectedCounters[$name] -ne 0) {
            throw "Test result counter '$name' is nonzero: $($rejectedCounters[$name])."
        }
    }

    $resultsNodes = @($trx.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']"))
    $unitResults = @($trx.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']"))
    $allUnitResults = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    if ($resultsNodes.Count -ne 1 -or $allUnitResults.Count -ne $unitResults.Count) {
        throw 'Test result must contain one Results element and no misplaced UnitTestResult elements.'
    }
    $passedResults = 0
    $failedResults = 0
    $skippedResults = 0
    foreach ($unitResult in $unitResults) {
        $resultOutcome = $unitResult.GetAttribute('outcome')
        switch ($resultOutcome) {
            'Passed' { $passedResults++ }
            'Failed' { $failedResults++ }
            { $_ -in @('NotExecuted', 'NotRunnable') } { $skippedResults++ }
            default { throw "Test result contains an unexpected outcome: '$resultOutcome'." }
        }
    }

    $skipped = $skippedResults
    if ($reportedSkipped -ne 0 -and $reportedSkipped -ne $skipped) {
        throw 'Skipped test counters are inconsistent with the result entries.'
    }
    if (
        $executed -gt $total -or
        $passed -gt $executed -or
        $failed -gt $executed -or
        ([long]$passed + [long]$failed) -ne [long]$executed -or
        ([long]$executed + [long]$skipped) -ne [long]$total
    ) {
        throw 'Test result counters are inconsistent.'
    }

    if ($unitResults.Count -ne [long]$executed + [long]$skipped) {
        throw "Test result entries ($($unitResults.Count)) do not match executed plus skipped count ($([long]$executed + [long]$skipped))."
    }
    if ($passedResults -ne $passed -or $failedResults -ne $failed) {
        throw 'Test result outcomes are inconsistent with the counters.'
    }
    [pscustomobject]@{
        Executed = $executed
        Passed = $passed
        Failed = $failed
        Skipped = $skipped
    }
}

function ConvertTo-EvidenceTimestamp {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
        $Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref] $parsed
    )) {
        throw "Evidence timestamp is malformed: '$Value'."
    }

    return $parsed.UtcDateTime.ToString(
        'yyyy-MM-ddTHH:mm:ss.fffffffZ',
        [Globalization.CultureInfo]::InvariantCulture
    )
}

function Get-EvidenceTimestamp {
    param(
        [string] $Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [DateTimeOffset]::UtcNow.UtcDateTime.ToString(
            'yyyy-MM-ddTHH:mm:ss.fffffffZ',
            [Globalization.CultureInfo]::InvariantCulture
        )
    }

    return ConvertTo-EvidenceTimestamp -Value $Value
}

function Get-FileSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [switch] $Upper
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "File is missing: $Path"
    }

    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if ($Upper) {
        return $hash.ToUpperInvariant()
    }
    return $hash.ToLowerInvariant()
}

function Get-StringSha256 {
    param(
        [Parameter(Mandatory)]
        [string] $Value,
        [switch] $Upper
    )

    $hash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))
    )
    if ($Upper) {
        return $hash.ToUpperInvariant()
    }
    return $hash.ToLowerInvariant()
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [Parameter(Mandatory)]
        [object] $Value
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $json = $Value | ConvertTo-Json -Depth 12
    [IO.File]::WriteAllText($Path, $json + "`n", [Text.UTF8Encoding]::new($false))
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "JSON file is missing: $Path"
    }

    try {
        return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    }
    catch {
        throw "JSON file is malformed: $Path"
    }
}

function Get-RepositoryName {
    param(
        [string] $Override
    )

    if (-not [string]::IsNullOrWhiteSpace($Override)) {
        if ($Override -notmatch '^[^/\s]+/[^/\s]+$') {
            throw "Repository must use owner/name form: '$Override'."
        }
        return $Override
    }

    $result = Invoke-NativeCommand -Command 'git' -Arguments @('remote', 'get-url', 'origin')
    if ($result.ExitCode -ne 0) {
        throw "git remote get-url origin failed with exit code $($result.ExitCode)."
    }

    $url = (($result.Output | ForEach-Object { $_.ToString() }) -join '').Trim()
    if ($url -notmatch 'github\.com[:/](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$') {
        throw "Origin remote is not a GitHub repository: '$url'."
    }
    return "$($Matches.owner)/$($Matches.repo)"
}

function Get-RuntimeProfile {
    param(
        [Parameter(Mandatory)]
        [string] $Root
    )

    if ([string]::IsNullOrWhiteSpace($Root)) {
        throw 'ProfileRoot is required.'
    }

    $profileRoot = [IO.Path]::GetFullPath($Root)
    if (-not (Test-Path -LiteralPath $profileRoot -PathType Container)) {
        throw "ProfileRoot is missing: $profileRoot"
    }

    $moddingRoot = if (Test-Path -LiteralPath (Join-Path $profileRoot 'Modding') -PathType Container) {
        Join-Path $profileRoot 'Modding'
    }
    else {
        $profileRoot
    }
    $pluginRoot = Join-Path $moddingRoot 'plugins'
    $entries = [Collections.Generic.List[object]]::new()
    $errors = [Collections.Generic.List[string]]::new()

    foreach ($dependency in $runtimeDependencies) {
        $path = Join-Path $pluginRoot $dependency.file
        $status = 'present'
        $sha256 = $null

        if (Test-Path -LiteralPath $path -PathType Leaf) {
            try {
                $sha256 = Get-FileSha256 -Path $path -Upper
            }
            catch {
                $status = 'unreadable'
                $errors.Add("$($dependency.logicalName) is unreadable: $path")
            }
        }
        elseif ($dependency.required) {
            $status = 'missing'
            $errors.Add("Required dependency is missing: $($dependency.file)")
        }
        else {
            $status = 'absent'
        }

        $entries.Add([ordered]@{
            logicalName = $dependency.logicalName
            file = "plugins/$($dependency.file)"
            status = $status
            sha256 = $sha256
        })
    }

    $canonical = @(
        $entries | ForEach-Object {
            if ($_.status -eq 'present') {
                "$($_.logicalName)|$($_.sha256)"
            }
            else {
                "$($_.logicalName)|$($_.status.ToUpperInvariant())"
            }
        }
    ) -join "`n"
    $profileHash = Get-StringSha256 -Value ($canonical + "`n") -Upper

    [pscustomobject]@{
        PluginRoot = $pluginRoot
        DeployedDllPath = Join-Path $pluginRoot 'EditorMode.dll'
        Entries = @($entries)
        ProfileHash = $profileHash
        Errors = @($errors)
    }
}

function Invoke-SmokeVerification {
    $errors = [Collections.Generic.List[string]]::new()
    $source = $null
    $profile = $null
    $deployedDllSha256 = $null

    try {
        if ([string]::IsNullOrWhiteSpace($SourceSha)) {
            $source = Get-SourceSha
        }
        elseif ($SourceSha -match '^[0-9a-f]{40}$') {
            $source = $SourceSha
        }
        else {
            throw "SourceSha is malformed: '$SourceSha'."
        }
    }
    catch {
        $errors.Add($_.Exception.Message)
    }

    try {
        $profile = Get-RuntimeProfile -Root $ProfileRoot
        foreach ($profileError in $profile.Errors) {
            $errors.Add($profileError)
        }
        if (Test-Path -LiteralPath $profile.DeployedDllPath -PathType Leaf) {
            $deployedDllSha256 = Get-FileSha256 -Path $profile.DeployedDllPath
        }
        else {
            $errors.Add("Deployed mod DLL is missing: $($profile.DeployedDllPath)")
        }
    }
    catch {
        $errors.Add($_.Exception.Message)
    }

    $status = if ($errors.Count -gt 0) {
        'FAIL'
    }
    else {
        $SmokeOutcome
    }
    $evidence = [ordered]@{
        schemaVersion = 1
        evidenceKind = 'runtime-smoke'
        generatedAt = Get-EvidenceTimestamp -Value $GeneratedAt
        sourceSha = $source
        deployedDllSha256 = $deployedDllSha256
        runtimeProfileHash = $(if ($profile) { $profile.ProfileHash } else { $null })
        dependencies = $(if ($profile) { @($profile.Entries) } else { @() })
        runtimeStatus = $status
        outcome = $status
        errors = @($errors)
    }

    Write-JsonFile -Path $smokeEvidencePath -Value $evidence
    Write-Host "Smoke evidence: $smokeEvidencePath"
    Write-Host "Runtime status: $status"
    if ($status -eq 'PASS') {
        return 0
    }
    return 1
}

function Copy-EvidenceFields {
    param(
        [Parameter(Mandatory)]
        [Collections.Specialized.OrderedDictionary] $Target,
        [Parameter(Mandatory)]
        [object] $Evidence,
        [Parameter(Mandatory)]
        [string[]] $Fields
    )

    foreach ($field in $Fields) {
        $property = $Evidence.PSObject.Properties[$field]
        if ($null -ne $property) {
            $Target[$field] = $property.Value
        }
    }
}

function Get-RequiredPropertyValue {
    param(
        [Parameter(Mandatory)]
        [object] $Object,
        [Parameter(Mandatory)]
        [string] $Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Evidence field is missing: $Name."
    }
    return $property.Value
}

function Assert-AutomatedEvidence {
    param(
        [Parameter(Mandatory)]
        [object] $Evidence
    )

    if (
        [int](Get-RequiredPropertyValue -Object $Evidence -Name 'schemaVersion') -ne 1 -or
        [string](Get-RequiredPropertyValue -Object $Evidence -Name 'evidenceKind') -ne 'automated'
    ) {
        throw 'Automated evidence has an invalid schema or evidence kind.'
    }
    if (
        [string](Get-RequiredPropertyValue -Object $Evidence -Name 'sourceSha') -notmatch '^[0-9a-f]{40}$' -or
        [string](Get-RequiredPropertyValue -Object $Evidence -Name 'buildDllSha256') -notmatch '^[0-9a-f]{64}$' -or
        [string](Get-RequiredPropertyValue -Object $Evidence -Name 'buildProfileDigest') -notmatch '^[0-9a-f]{64}$'
    ) {
        throw 'Automated evidence has malformed SHA fields.'
    }
    [void](ConvertTo-EvidenceTimestamp -Value ([string](Get-RequiredPropertyValue -Object $Evidence -Name 'generatedAt')))

    $status = [string](Get-RequiredPropertyValue -Object $Evidence -Name 'automatedStatus')
    if ($status -notin @('PASS', 'FAIL', 'PARTIAL')) {
        throw "Automated evidence has an invalid status: '$status'."
    }

    $tests = Get-RequiredPropertyValue -Object $Evidence -Name 'tests'
    if ($null -eq $tests) {
        throw 'Automated evidence tests are missing.'
    }
    $testsProject = [string](Get-RequiredPropertyValue -Object $tests -Name 'project')
    if ([string]::IsNullOrWhiteSpace($testsProject)) {
        throw 'Automated evidence test project is missing.'
    }
    if ($testsProject -ne $testProjectRelative) {
        throw "Automated evidence test project '$testsProject' does not match '$testProjectRelative'."
    }
    $testsStatus = [string](Get-RequiredPropertyValue -Object $tests -Name 'status')
    if ($testsStatus -notin @('PASS', 'FAIL', 'NOT_RUN')) {
        throw "Automated evidence has an invalid test status: '$testsStatus'."
    }
    $executed = [int](Get-RequiredPropertyValue -Object $tests -Name 'executed')
    $passed = [int](Get-RequiredPropertyValue -Object $tests -Name 'passed')
    $failed = [int](Get-RequiredPropertyValue -Object $tests -Name 'failed')
    $skipped = [int](Get-RequiredPropertyValue -Object $tests -Name 'skipped')
    if ($executed -lt 0 -or $passed -lt 0 -or $failed -lt 0 -or $skipped -lt 0) {
        throw 'Automated evidence test counts cannot be negative.'
    }

    $whitespace = Get-RequiredPropertyValue -Object $Evidence -Name 'whitespace'
    if ($null -eq $whitespace) {
        throw 'Automated evidence whitespace result is missing.'
    }
    $whitespaceStatus = [string](Get-RequiredPropertyValue -Object $whitespace -Name 'status')
    if ($whitespaceStatus -notin @('PASS', 'FAIL', 'NOT_RUN')) {
        throw "Automated evidence has an invalid whitespace status: '$whitespaceStatus'."
    }
    $checkedFiles = [int](Get-RequiredPropertyValue -Object $whitespace -Name 'checkedFiles')
    if ($checkedFiles -lt 0) {
        throw 'Automated evidence checked file count cannot be negative.'
    }
    $findings = @(Get-RequiredPropertyValue -Object $whitespace -Name 'findings')
    [void](Get-RequiredPropertyValue -Object $Evidence -Name 'sdkVersion')
    $errors = @(Get-RequiredPropertyValue -Object $Evidence -Name 'errors')
    $runner = Get-RequiredPropertyValue -Object $Evidence -Name 'runner'
    if ($null -eq $runner) {
        throw 'Automated evidence runner identity is missing.'
    }
    $runnerFields = @('os', 'osVersion', 'architecture', 'powershellVersion', 'machineName')
    foreach ($field in $runnerFields) {
        [void](Get-RequiredPropertyValue -Object $runner -Name $field)
    }

    if ($status -eq 'PASS') {
        if (
            [string](Get-RequiredPropertyValue -Object $Evidence -Name 'sourceTreeStatus') -ne 'clean' -or
            $testsStatus -ne 'PASS' -or
            $executed -eq 0 -or
            $passed -ne $executed -or
            $failed -ne 0 -or
            $whitespaceStatus -ne 'PASS' -or
            $checkedFiles -eq 0 -or
            $findings.Count -ne 0 -or
            [string]::IsNullOrWhiteSpace([string](Get-RequiredPropertyValue -Object $Evidence -Name 'sdkVersion')) -or
            $errors.Count -ne 0 -or
            @($runnerFields | Where-Object {
                [string]::IsNullOrWhiteSpace([string]$runner.PSObject.Properties[$_].Value)
            }).Count -ne 0
        ) {
            throw 'Automated PASS evidence is internally inconsistent.'
        }
    }
}

function Assert-AutomatedConclusion {
    param(
        [Parameter(Mandatory)]
        [string] $Conclusion,
        [Parameter(Mandatory)]
        [string] $AutomatedStatus
    )

    if (
        ($Conclusion -eq 'success' -and $AutomatedStatus -ne 'PASS') -or
        ($Conclusion -eq 'failure' -and $AutomatedStatus -eq 'PASS')
    ) {
        throw 'Verification workflow conclusion disagrees with automated evidence.'
    }
}

function Invoke-TransportCreation {
    if ($PrNumber -le 0) {
        throw 'PrNumber must be a positive issue number.'
    }
    if ([string]::IsNullOrWhiteSpace($SourceSha) -or $SourceSha -notmatch '^[0-9a-f]{40}$') {
        throw "SourceSha is malformed: '$SourceSha'."
    }
    if ($WorkflowRunId -le 0) {
        throw 'WorkflowRunId must be positive.'
    }
    if ($WorkflowRunAttempt -le 0) {
        throw 'WorkflowRunAttempt must be positive.'
    }
    foreach ($workflowField in @(
        @{ Name = 'WorkflowName'; Value = $WorkflowName }
        @{ Name = 'WorkflowPath'; Value = $WorkflowPath }
        @{ Name = 'WorkflowEvent'; Value = $WorkflowEvent }
        @{ Name = 'WorkflowConclusion'; Value = $WorkflowConclusion }
    )) {
        if ([string]::IsNullOrWhiteSpace([string]$workflowField.Value)) {
            throw "$($workflowField.Name) is required."
        }
    }

    $repositoryName = Get-RepositoryName -Override $Repository
    $evidence = Read-JsonFile -Path $evidencePath
    Assert-AutomatedEvidence -Evidence $evidence
    if ([string]$evidence.sourceSha -ne $SourceSha) {
        throw "Verified source SHA '$($evidence.sourceSha)' does not match the reviewed head SHA '$SourceSha'."
    }
    Assert-AutomatedConclusion -Conclusion $WorkflowConclusion `
        -AutomatedStatus ([string]$evidence.automatedStatus)

    $transport = [ordered]@{
        schemaVersion = 1
        transportVersion = 1
        authoritative = $false
        generatedAt = [string]$evidence.generatedAt
        repository = $repositoryName
        prNumber = $PrNumber
        sourceSha = $SourceSha
        workflow = [ordered]@{
            name = $WorkflowName
            path = $WorkflowPath
            event = $WorkflowEvent
            runId = $WorkflowRunId
            runAttempt = $WorkflowRunAttempt
            conclusion = $WorkflowConclusion
        }
        evidence = $evidence
    }

    Write-JsonFile -Path $transportOutputPath -Value $transport
    Write-Host "Verification transport: $transportOutputPath"
    return 0
}

function Get-PublicationRecord {
    param(
        [object] $AutomatedEvidence,
        [switch] $UseSuppliedEvidence
    )

    $automated = if ($UseSuppliedEvidence) {
        $AutomatedEvidence
    }
    elseif (Test-Path -LiteralPath $evidencePath -PathType Leaf) {
        Read-JsonFile -Path $evidencePath
    }
    else {
        $null
    }
    $smoke = if ($UseSuppliedEvidence) {
        $null
    }
    elseif (Test-Path -LiteralPath $smokeEvidencePath -PathType Leaf) {
        Read-JsonFile -Path $smokeEvidencePath
    }
    else {
        $null
    }

    $primary = if ($EvidenceKind -eq 'runtime-smoke') { $smoke } else { $automated }
    if ($null -eq $primary) {
        throw "Requested evidence is missing for kind '$EvidenceKind'."
    }
    if ($null -ne $automated) {
        Assert-AutomatedEvidence -Evidence $automated
    }
    if ($null -ne $smoke -and (
        [int]$smoke.schemaVersion -ne 1 -or
        [string]$smoke.evidenceKind -ne 'runtime-smoke'
    )) {
        throw 'Smoke evidence has an invalid schema or evidence kind.'
    }
    $sourceSha = [string]$primary.sourceSha
    if ($sourceSha -notmatch '^[0-9a-f]{40}$') {
        throw "Evidence source SHA is malformed: '$sourceSha'."
    }
    $generatedAt = ConvertTo-EvidenceTimestamp -Value ([string]$primary.generatedAt)

    if ($EvidenceKind -eq 'runtime-smoke') {
        if ([string]$primary.evidenceKind -ne 'runtime-smoke') {
            throw 'Smoke evidence has the wrong evidence kind.'
        }
        if ([string]$primary.deployedDllSha256 -notmatch '^[0-9a-f]{64}$' -or
            [string]$primary.runtimeProfileHash -notmatch '^[0-9A-F]{64}$') {
            throw 'Smoke evidence hashes are malformed.'
        }
    }
    else {
        if ([string]$primary.evidenceKind -ne 'automated') {
            throw 'Automated evidence has the wrong evidence kind.'
        }
        if ([string]$primary.buildDllSha256 -notmatch '^[0-9a-f]{64}$' -or
            [string]$primary.buildProfileDigest -notmatch '^[0-9a-f]{64}$') {
            throw 'Automated evidence hashes are malformed.'
        }
    }

    $automatedStatus = 'PARTIAL'
    $automatedGeneratedAt = $null
    if ($null -ne $automated -and [string]$automated.sourceSha -eq $sourceSha) {
        $automatedGeneratedAt = ConvertTo-EvidenceTimestamp -Value ([string]$automated.generatedAt)
        $automatedStatus = if ([string]$automated.automatedStatus -in @('PASS', 'FAIL')) {
            [string]$automated.automatedStatus
        }
        else {
            'PARTIAL'
        }
    }

    $runtimeStatus = 'PARTIAL'
    $smokeGeneratedAt = $null
    if ($null -ne $smoke -and [string]$smoke.sourceSha -eq $sourceSha) {
        $smokeGeneratedAt = ConvertTo-EvidenceTimestamp -Value ([string]$smoke.generatedAt)
        $smokeOutcome = [string]$smoke.outcome
        $smokeRuntimeStatus = [string]$smoke.runtimeStatus
        if (
            $smokeOutcome -notin @('PASS', 'FAIL', 'PARTIAL') -or
            $smokeRuntimeStatus -notin @('PASS', 'FAIL', 'PARTIAL')
        ) {
            throw 'Smoke evidence has an invalid runtime status.'
        }
        if ($smokeOutcome -ne $smokeRuntimeStatus) {
            throw 'Smoke evidence has inconsistent runtime status fields.'
        }
        $runtimeStatus = $smokeOutcome
        if (
            $runtimeStatus -eq 'PASS' -and
            $null -ne $automatedGeneratedAt -and
            [DateTimeOffset]::Parse($smokeGeneratedAt) -lt [DateTimeOffset]::Parse($automatedGeneratedAt)
        ) {
            $runtimeStatus = 'PARTIAL'
        }
    }

    $overallStatus = if ($automatedStatus -eq 'FAIL' -or $runtimeStatus -eq 'FAIL') {
        'FAIL'
    }
    elseif ($automatedStatus -eq 'PASS' -and $runtimeStatus -eq 'PASS') {
        'PASS'
    }
    else {
        'PARTIAL'
    }

    $record = [ordered]@{
        schemaVersion = 1
        evidenceKind = $EvidenceKind
        sourceSha = $sourceSha
        generatedAt = $generatedAt
        automatedStatus = $automatedStatus
        runtimeStatus = $runtimeStatus
        overallStatus = $overallStatus
    }
    Copy-EvidenceFields -Target $record -Evidence $primary -Fields @(
        'sourceTreeStatus'
        'buildDllSha256'
        'buildProfileDigest'
        'tests'
        'whitespace'
        'sdkVersion'
        'runner'
        'deployedDllSha256'
        'runtimeProfileHash'
        'dependencies'
        'outcome'
        'errors'
    )
    return $record
}

function Get-PrMetadata {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryName,
        [Parameter(Mandatory)]
        [int] $Number
    )

    $result = Invoke-NativeCommand -Command 'gh' -Arguments @(
        'pr'
        'view'
        "$Number"
        '--repo'
        $RepositoryName
        '--json'
        'body,headRefOid,baseRefName,state,isDraft'
    )
    if ($result.ExitCode -ne 0) {
        throw "gh pr view failed with exit code $($result.ExitCode)."
    }

    try {
        $metadata = (($result.Output | ForEach-Object { $_.ToString() }) -join "`n") | ConvertFrom-Json
    }
    catch {
        throw 'Pull request metadata is malformed.'
    }

    $matches = @([regex]::Matches(
        [string]$metadata.body,
        '(?im)^[ \t]*Evidence issue:[ \t]*#(?<number>\d+)[ \t]*$'
    ))
    if ($matches.Count -ne 1) {
        throw "Pull request must contain exactly one 'Evidence issue: #N' line; found $($matches.Count)."
    }

    $evidenceIssue = [int]$matches[0].Groups['number'].Value
    if ($evidenceIssue -eq 1) {
        throw 'Issue #1 is not a valid evidence issue.'
    }

    [pscustomobject]@{
        HeadRefOid = [string]$metadata.headRefOid
        BaseRefName = [string]$metadata.baseRefName
        State = [string]$metadata.state
        IsDraft = [bool]$metadata.isDraft
        EvidenceIssue = $evidenceIssue
    }
}

function Get-GitHubWorkflowRun {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryName
    )

    if ($WorkflowRunId -le 0) {
        throw 'WorkflowRunId must be positive.'
    }
    if ($WorkflowRunAttempt -le 0) {
        throw 'WorkflowRunAttempt must be positive.'
    }

    $result = Invoke-NativeCommand -Command 'gh' -Arguments @(
        'api'
        '--method'
        'GET'
        "repos/$RepositoryName/actions/runs/$WorkflowRunId"
    )
    if ($result.ExitCode -ne 0) {
        throw "Workflow run lookup failed with exit code $($result.ExitCode)."
    }
    try {
        $run = (($result.Output | ForEach-Object { $_.ToString() }) -join "`n") | ConvertFrom-Json
    }
    catch {
        throw 'Workflow run response is malformed.'
    }

    $runRepository = Get-RequiredPropertyValue -Object $run -Name 'repository'
    if ([string](Get-RequiredPropertyValue -Object $runRepository -Name 'full_name') -ne $RepositoryName) {
        throw 'Workflow run repository does not match the publisher repository.'
    }
    if (
        [long](Get-RequiredPropertyValue -Object $run -Name 'id') -ne $WorkflowRunId -or
        [int](Get-RequiredPropertyValue -Object $run -Name 'run_attempt') -ne $WorkflowRunAttempt
    ) {
        throw 'Workflow run identity does not match the publisher inputs.'
    }
    if (
        [string](Get-RequiredPropertyValue -Object $run -Name 'name') -ne $WorkflowName -or
        [string](Get-RequiredPropertyValue -Object $run -Name 'path') -ne $WorkflowPath -or
        [string](Get-RequiredPropertyValue -Object $run -Name 'event') -ne $WorkflowEvent
    ) {
        throw 'Workflow run does not match the approved verification workflow.'
    }
    if ([string](Get-RequiredPropertyValue -Object $run -Name 'status') -ne 'completed') {
        throw 'Workflow run is not completed.'
    }
    $conclusion = [string](Get-RequiredPropertyValue -Object $run -Name 'conclusion')
    if ([string]::IsNullOrWhiteSpace($conclusion)) {
        throw 'Workflow run conclusion is missing.'
    }

    $pullRequests = @(Get-RequiredPropertyValue -Object $run -Name 'pull_requests')
    if ($pullRequests.Count -gt 1) {
        throw "Workflow run must be associated with at most one pull request; found $($pullRequests.Count)."
    }

    $pullRequestNumber = 0
    $sourceSha = $null
    $headRepositoryName = $null
    $baseRef = $null
    if ($pullRequests.Count -eq 1) {
        $association = $pullRequests[0]
        $pullRequestNumber = [int](Get-RequiredPropertyValue -Object $association -Name 'number')
        $head = Get-RequiredPropertyValue -Object $association -Name 'head'
        $base = Get-RequiredPropertyValue -Object $association -Name 'base'
        $headRepository = Get-RequiredPropertyValue -Object $head -Name 'repo'
        $sourceSha = [string](Get-RequiredPropertyValue -Object $head -Name 'sha')
        $headRepositoryName = [string](Get-RequiredPropertyValue -Object $headRepository -Name 'full_name')
        $baseRef = [string](Get-RequiredPropertyValue -Object $base -Name 'ref')
    }
    else {
        $runHeadRepository = Get-RequiredPropertyValue -Object $run -Name 'head_repository'
        $headRepositoryName = [string](Get-RequiredPropertyValue -Object $runHeadRepository -Name 'full_name')
        $headBranch = [string](Get-RequiredPropertyValue -Object $run -Name 'head_branch')
        $sourceSha = [string](Get-RequiredPropertyValue -Object $run -Name 'head_sha')
        if (
            $headRepositoryName -notmatch '^[^/\s]+/[^/\s]+$' -or
            [string]::IsNullOrWhiteSpace($headBranch) -or
            $sourceSha -notmatch '^[0-9a-f]{40}$'
        ) {
            throw 'Workflow run head identity is malformed.'
        }

        $headOwner = ($headRepositoryName -split '/')[0]
        $pullResult = Invoke-NativeCommand -Command 'gh' -Arguments @(
            'api'
            '--method'
            'GET'
            "repos/$RepositoryName/pulls"
            '--raw-field'
            'state=open'
            '--raw-field'
            'base=dev'
            '--raw-field'
            "head=$headOwner`:$headBranch"
        )
        if ($pullResult.ExitCode -ne 0) {
            throw "Workflow run pull request lookup failed with exit code $($pullResult.ExitCode)."
        }
        try {
            $pullList = (($pullResult.Output | ForEach-Object { $_.ToString() }) -join "`n") | ConvertFrom-Json
            $pullCandidates = @($pullList)
        }
        catch {
            throw 'Workflow run pull request lookup response is malformed.'
        }
        if ($pullCandidates.Count -ne 1) {
            throw "Workflow run must resolve to exactly one open pull request; found $($pullCandidates.Count)."
        }

        $pull = $pullCandidates[0]
        $pullRequestNumber = [int](Get-RequiredPropertyValue -Object $pull -Name 'number')
        $pullHead = Get-RequiredPropertyValue -Object $pull -Name 'head'
        $pullBase = Get-RequiredPropertyValue -Object $pull -Name 'base'
        $pullHeadRepository = Get-RequiredPropertyValue -Object $pullHead -Name 'repo'
        $pullHeadRepositoryName = [string](
            Get-RequiredPropertyValue -Object $pullHeadRepository -Name 'full_name'
        )
        $pullHeadRef = [string](Get-RequiredPropertyValue -Object $pullHead -Name 'ref')
        $pullSourceSha = [string](Get-RequiredPropertyValue -Object $pullHead -Name 'sha')
        $baseRef = [string](Get-RequiredPropertyValue -Object $pullBase -Name 'ref')
        $pullState = [string](Get-RequiredPropertyValue -Object $pull -Name 'state')
        if (
            $pullRequestNumber -le 0 -or
            $pullState -ne 'open' -or
            $pullSourceSha -ne $sourceSha -or
            $pullHeadRepositoryName -ne $headRepositoryName -or
            $pullHeadRef -ne $headBranch
        ) {
            throw 'Workflow run pull request association does not match the run head.'
        }
        $sourceSha = $pullSourceSha
    }

    if ($pullRequestNumber -le 0 -or $sourceSha -notmatch '^[0-9a-f]{40}$') {
        throw 'Workflow run has malformed pull request association.'
    }
    if ($headRepositoryName -notmatch '^[^/\s]+/[^/\s]+$' -or $baseRef -ne 'dev') {
        throw 'Workflow run pull request repository or base branch is invalid.'
    }

    return [pscustomobject]@{
        Id = [long]$WorkflowRunId
        RunAttempt = [int]$WorkflowRunAttempt
        Name = [string]$run.name
        Path = [string]$run.path
        Event = [string]$run.event
        Conclusion = $conclusion
        CreatedAt = ConvertTo-EvidenceTimestamp -Value ([string](
            Get-RequiredPropertyValue -Object $run -Name 'created_at'
        ))
        PrNumber = $pullRequestNumber
        SourceSha = $sourceSha
        HeadRepository = $headRepositoryName
        BaseRef = $baseRef
    }
}

function Assert-WorkflowArtifact {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryName,
        [Parameter(Mandatory)]
        [object] $Run
    )

    $artifactName = "pr-verification-$($Run.Id)-$($Run.RunAttempt)"
    $result = Invoke-NativeCommand -Command 'gh' -Arguments @(
        'api'
        '--method'
        'GET'
        "repos/$RepositoryName/actions/runs/$($Run.Id)/artifacts"
        '--raw-field'
        "name=$artifactName"
        '--raw-field'
        'per_page=100'
    )
    if ($result.ExitCode -ne 0) {
        throw "Workflow artifact lookup failed with exit code $($result.ExitCode)."
    }
    try {
        $artifactList = (($result.Output | ForEach-Object { $_.ToString() }) -join "`n") | ConvertFrom-Json
        $artifacts = @(Get-RequiredPropertyValue -Object $artifactList -Name 'artifacts')
    }
    catch {
        throw 'Workflow artifact response is malformed.'
    }

    $artifactMatches = @($artifacts | Where-Object { [string]$_.name -ceq $artifactName })
    if ($artifactMatches.Count -ne 1) {
        throw "Workflow run must contain exactly one '$artifactName' artifact; found $($artifactMatches.Count)."
    }
    $artifact = $artifactMatches[0]
    if ([bool](Get-RequiredPropertyValue -Object $artifact -Name 'expired')) {
        throw 'Workflow transport artifact is expired.'
    }
    $size = [long](Get-RequiredPropertyValue -Object $artifact -Name 'size_in_bytes')
    if ($TransportMaxBytes -le 0 -or $size -le 0 -or $size -gt $TransportMaxBytes) {
        throw "Workflow transport artifact is empty or too large: $size bytes."
    }
    $artifactRun = Get-RequiredPropertyValue -Object $artifact -Name 'workflow_run'
    if ([long](Get-RequiredPropertyValue -Object $artifactRun -Name 'id') -ne $Run.Id) {
        throw 'Workflow transport artifact belongs to a different run.'
    }

    $now = [DateTimeOffset]::UtcNow
    $created = [DateTimeOffset]::Parse($(
        ConvertTo-EvidenceTimestamp -Value ([string](
            Get-RequiredPropertyValue -Object $artifact -Name 'created_at'
        ))
    ))
    $runCreated = [DateTimeOffset]::Parse($Run.CreatedAt)
    $expires = [DateTimeOffset]::Parse($(
        ConvertTo-EvidenceTimestamp -Value ([string](
            Get-RequiredPropertyValue -Object $artifact -Name 'expires_at'
        ))
    ))
    if ($created -lt $runCreated.AddMinutes(-5) -or $created -gt $now.AddMinutes(5)) {
        throw 'Workflow transport artifact timestamp is inconsistent with its run.'
    }
    if ($expires -le $now) {
        throw 'Workflow transport artifact has expired.'
    }

    return [pscustomobject]@{
        Id = [long](Get-RequiredPropertyValue -Object $artifact -Name 'id')
        Name = $artifactName
        Size = $size
        CreatedAt = $created.UtcDateTime.ToString('O')
    }
}

function Assert-EvidenceIssue {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryName,
        [Parameter(Mandatory)]
        [int] $Number
    )

    if ($Number -le 1) {
        throw 'Issue #1 and lower are not valid evidence issues.'
    }
    $result = Invoke-NativeCommand -Command 'gh' -Arguments @(
        'api'
        '--method'
        'GET'
        "repos/$RepositoryName/issues/$Number"
    )
    if ($result.ExitCode -ne 0) {
        throw "Evidence issue #$Number cannot be read."
    }
    try {
        $issue = (($result.Output | ForEach-Object { $_.ToString() }) -join "`n") | ConvertFrom-Json
    }
    catch {
        throw 'Evidence issue response is malformed.'
    }
    if ([int](Get-RequiredPropertyValue -Object $issue -Name 'number') -ne $Number) {
        throw 'Evidence issue number does not match the PR declaration.'
    }
    $pullRequestProperty = $issue.PSObject.Properties['pull_request']
    if ($null -ne $pullRequestProperty -and $null -ne $pullRequestProperty.Value) {
        throw 'Evidence issue declaration points to a pull request.'
    }
}

function Read-PrTransportRecord {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryName,
        [Parameter(Mandatory)]
        [object] $Run
    )

    if ([string]::IsNullOrWhiteSpace($transportInputPath)) {
        throw 'TransportPath is required.'
    }
    if (-not (Test-Path -LiteralPath $transportInputPath -PathType Leaf)) {
        throw "Verification transport is missing: $transportInputPath"
    }
    $size = (Get-Item -LiteralPath $transportInputPath).Length
    if ($TransportMaxBytes -le 0 -or $size -gt $TransportMaxBytes) {
        throw "Verification transport is too large: $size bytes."
    }

    $transport = Read-JsonFile -Path $transportInputPath
    if (
        [int](Get-RequiredPropertyValue -Object $transport -Name 'schemaVersion') -ne 1 -or
        [int](Get-RequiredPropertyValue -Object $transport -Name 'transportVersion') -ne 1 -or
        [bool](Get-RequiredPropertyValue -Object $transport -Name 'authoritative')
    ) {
        throw 'Verification transport has an invalid schema or authority marker.'
    }
    if ([string](Get-RequiredPropertyValue -Object $transport -Name 'repository') -ne $RepositoryName) {
        throw 'Verification transport repository does not match the publisher repository.'
    }
    if ([int](Get-RequiredPropertyValue -Object $transport -Name 'prNumber') -ne $Run.PrNumber) {
        throw 'Verification transport PR number does not match the authoritative run.'
    }
    if ([string](Get-RequiredPropertyValue -Object $transport -Name 'sourceSha') -ne $Run.SourceSha) {
        throw 'Verification transport source SHA does not match the authoritative run.'
    }

    $workflow = Get-RequiredPropertyValue -Object $transport -Name 'workflow'
    if ($null -eq $workflow) {
        throw 'Verification transport workflow identity is missing.'
    }
    $identityMatches = (
        [string](Get-RequiredPropertyValue -Object $workflow -Name 'name') -eq $Run.Name -and
        [string](Get-RequiredPropertyValue -Object $workflow -Name 'path') -eq $Run.Path -and
        [string](Get-RequiredPropertyValue -Object $workflow -Name 'event') -eq $Run.Event -and
        [long](Get-RequiredPropertyValue -Object $workflow -Name 'runId') -eq $Run.Id -and
        [int](Get-RequiredPropertyValue -Object $workflow -Name 'runAttempt') -eq $Run.RunAttempt -and
        [string](Get-RequiredPropertyValue -Object $workflow -Name 'conclusion') -eq $Run.Conclusion
    )
    if (-not $identityMatches) {
        throw 'Verification transport workflow identity does not match the authoritative run.'
    }

    $generatedAt = ConvertTo-EvidenceTimestamp -Value ([string](
        Get-RequiredPropertyValue -Object $transport -Name 'generatedAt'
    ))
    $generated = [DateTimeOffset]::Parse($generatedAt)
    $now = [DateTimeOffset]::UtcNow
    $runCreated = [DateTimeOffset]::Parse($Run.CreatedAt)
    if ($generated -lt $now.AddMinutes(-$FreshnessMinutes)) {
        throw "Verification transport is stale: generated at $generatedAt."
    }
    if ($generated -lt $runCreated.AddMinutes(-5)) {
        throw 'Verification transport predates its authoritative workflow run.'
    }
    if ($generated -gt $now.AddMinutes(5)) {
        throw "Verification transport timestamp is in the future: $generatedAt."
    }

    $evidence = Get-RequiredPropertyValue -Object $transport -Name 'evidence'
    Assert-AutomatedEvidence -Evidence $evidence
    if ([string]$evidence.sourceSha -ne $Run.SourceSha) {
        throw 'Verification transport evidence source SHA does not match the authoritative run.'
    }
    $evidenceGeneratedAt = ConvertTo-EvidenceTimestamp -Value ([string](
        Get-RequiredPropertyValue -Object $evidence -Name 'generatedAt'
    ))
    if ($evidenceGeneratedAt -ne $generatedAt) {
        throw 'Verification transport and evidence timestamps disagree.'
    }
    Assert-AutomatedConclusion -Conclusion $Run.Conclusion `
        -AutomatedStatus ([string]$evidence.automatedStatus)

    return $transport
}

function Set-CommitCheck {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryName,
        [Parameter(Mandatory)]
        [string] $Sha,
        [Parameter(Mandatory)]
        [ValidateSet('success', 'failure')]
        [string] $Conclusion,
        [Parameter(Mandatory)]
        [string] $Title,
        [Parameter(Mandatory)]
        [string] $Summary
    )

    if ($CheckContext -notmatch '^\S(?:.*\S)?$') {
        throw 'CheckContext is empty.'
    }
    if ($Sha -notmatch '^[0-9a-f]{40}$') {
        throw "Check SHA is malformed: '$Sha'."
    }

    $query = Invoke-NativeCommand -Command 'gh' -Arguments @(
        'api'
        '--method'
        'GET'
        "repos/$RepositoryName/commits/$Sha/check-runs"
        '--raw-field'
        "check_name=$CheckContext"
        '--raw-field'
        'per_page=100'
    )
    if ($query.ExitCode -ne 0) {
        throw "Existing check lookup failed with exit code $($query.ExitCode)."
    }
    try {
        $checkList = (($query.Output | ForEach-Object { $_.ToString() }) -join "`n") | ConvertFrom-Json
        $existingRuns = @(Get-RequiredPropertyValue -Object $checkList -Name 'check_runs')
    }
    catch {
        throw 'Existing check response is malformed.'
    }

    $existing = @(
        $existingRuns |
            Where-Object { [string]$_.name -eq $CheckContext } |
            Sort-Object -Property id -Descending |
            Select-Object -First 1
    )
    $output = [ordered]@{
        title = $Title
        summary = $Summary
    }
    if ($existing.Count -eq 1) {
        $checkId = [long]$existing[0].id
        $method = 'PATCH'
        $endpoint = "repos/$RepositoryName/check-runs/$checkId"
        $request = [ordered]@{
            status = 'completed'
            conclusion = $Conclusion
            output = $output
        }
    }
    else {
        $method = 'POST'
        $endpoint = "repos/$RepositoryName/check-runs"
        $request = [ordered]@{
            name = $CheckContext
            head_sha = $Sha
            status = 'completed'
            conclusion = $Conclusion
            output = $output
        }
    }

    Write-JsonFile -Path $checkRunRequestPath -Value $request
    $result = Invoke-NativeCommand -Command 'gh' -Arguments @(
        'api'
        $endpoint
        '--method'
        $method
        '--input'
        $checkRunRequestPath
    )
    if ($result.ExitCode -ne 0) {
        throw "Check publication failed with exit code $($result.ExitCode)."
    }
    Write-Host "Check ${Conclusion}: $CheckContext @ $Sha"
}

function Get-TargetComments {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryName,
        [Parameter(Mandatory)]
        [int] $TargetNumber
    )

    $result = Invoke-NativeCommand -Command 'gh' -Arguments @(
        'api'
        '--method'
        'GET'
        '--paginate'
        '--slurp'
        "repos/$RepositoryName/issues/$TargetNumber/comments"
        '--raw-field'
        'per_page=100'
    )
    if ($result.ExitCode -ne 0) {
        throw "gh api comments failed with exit code $($result.ExitCode)."
    }

    try {
        $comments = (($result.Output | ForEach-Object { $_.ToString() }) -join "`n") | ConvertFrom-Json
    }
    catch {
        throw 'Existing comments are malformed.'
    }
    return @($comments | ForEach-Object { $_ })
}

function Get-CommentEvidenceRecord {
    param(
        [Parameter(Mandatory)]
        [string] $Body
    )

    $matches = @([regex]::Matches(
        $Body,
        '(?s)<!-- codex-evidence-json:start -->\r?\n(?<json>.*?)\r?\n<!-- codex-evidence-json:end -->'
    ))
    if ($matches.Count -ne 1) {
        throw 'Existing marker comment has malformed evidence JSON delimiters.'
    }

    try {
        return $matches[0].Groups['json'].Value | ConvertFrom-Json
    }
    catch {
        throw 'Existing marker comment has malformed evidence JSON.'
    }
}

function New-EvidenceCommentBody {
    param(
        [Parameter(Mandatory)]
        [object] $Record
    )

    $json = $Record | ConvertTo-Json -Depth 12 -Compress
    $lines = @(
        "<!-- codex-evidence:$EvidenceKind -->"
        "## Verification evidence: $EvidenceKind"
        ''
        ('- Source SHA: `{0}`' -f $Record.sourceSha)
        ('- Automated: `{0}`' -f $Record.automatedStatus)
        ('- Runtime: `{0}`' -f $Record.runtimeStatus)
        ('- Overall: `{0}`' -f $Record.overallStatus)
        ''
        '<!-- codex-evidence-json:start -->'
        $json
        '<!-- codex-evidence-json:end -->'
    )
    return $lines -join "`n"
}

function Assert-PublicationFreshness {
    param(
        [Parameter(Mandatory)]
        [object] $ExistingRecord,
        [Parameter(Mandatory)]
        [object] $IncomingRecord
    )

    $existingRunCreatedProperty = $ExistingRecord.PSObject.Properties['workflowRunCreatedAt']
    $incomingRunCreatedProperty = $IncomingRecord.PSObject.Properties['workflowRunCreatedAt']
    if (
        $null -ne $existingRunCreatedProperty -and
        $null -ne $incomingRunCreatedProperty -and
        -not [string]::IsNullOrWhiteSpace([string]$existingRunCreatedProperty.Value) -and
        -not [string]::IsNullOrWhiteSpace([string]$incomingRunCreatedProperty.Value)
    ) {
        $existingRunCreated = [DateTimeOffset]::Parse($(
            ConvertTo-EvidenceTimestamp -Value ([string]$existingRunCreatedProperty.Value)
        ))
        $incomingRunCreated = [DateTimeOffset]::Parse($(
            ConvertTo-EvidenceTimestamp -Value ([string]$incomingRunCreatedProperty.Value)
        ))
        if ($existingRunCreated -gt $incomingRunCreated) {
            throw 'Stale publication: an older verification run cannot replace newer evidence.'
        }
        if ($existingRunCreated -eq $incomingRunCreated) {
            $existingRunId = [long]$ExistingRecord.workflowRunId
            $incomingRunId = [long]$IncomingRecord.workflowRunId
            $existingAttempt = [int]$ExistingRecord.workflowRunAttempt
            $incomingAttempt = [int]$IncomingRecord.workflowRunAttempt
            if (
                $existingRunId -gt $incomingRunId -or
                ($existingRunId -eq $incomingRunId -and $existingAttempt -ge $incomingAttempt)
            ) {
                throw 'Stale publication: an older verification attempt cannot replace newer evidence.'
            }
        }
        return
    }

    $existingGeneratedAt = ConvertTo-EvidenceTimestamp -Value ([string]$ExistingRecord.generatedAt)
    $incomingGeneratedAt = ConvertTo-EvidenceTimestamp -Value ([string]$IncomingRecord.generatedAt)
    if ($existingGeneratedAt -ge $incomingGeneratedAt) {
        throw 'Stale publication: an older record cannot replace newer evidence.'
    }
}

function Test-NewerPublishedEvidence {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryName,
        [Parameter(Mandatory)]
        [object] $Run
    )

    try {
        $comments = Get-TargetComments -RepositoryName $RepositoryName -TargetNumber $Run.PrNumber
        $marker = "<!-- codex-evidence:$EvidenceKind -->"
        $markerComments = @($comments | Where-Object { [string]$_.body -like "*$marker*" })
        if ($markerComments.Count -eq 0) {
            return $false
        }
        if ($markerComments.Count -ne 1) {
            return $true
        }

        $existingRecord = Get-CommentEvidenceRecord -Body ([string]$markerComments[0].body)
        if ([string]$existingRecord.evidenceKind -ne $EvidenceKind) {
            return $true
        }
        if ([string]$existingRecord.sourceSha -ne $Run.SourceSha) {
            return $false
        }

        $incomingCreated = [DateTimeOffset]::Parse($Run.CreatedAt)
        $existingRunCreatedProperty = $existingRecord.PSObject.Properties['workflowRunCreatedAt']
        if (
            $null -ne $existingRunCreatedProperty -and
            -not [string]::IsNullOrWhiteSpace([string]$existingRunCreatedProperty.Value)
        ) {
            $existingCreated = [DateTimeOffset]::Parse($(
                ConvertTo-EvidenceTimestamp -Value ([string]$existingRunCreatedProperty.Value)
            ))
            if ($existingCreated -ne $incomingCreated) {
                return $existingCreated -gt $incomingCreated
            }
            $existingRunId = [long]$existingRecord.workflowRunId
            $existingAttempt = [int]$existingRecord.workflowRunAttempt
            return (
                $existingRunId -gt $Run.Id -or
                ($existingRunId -eq $Run.Id -and $existingAttempt -gt $Run.RunAttempt)
            )
        }

        $existingGeneratedAt = ConvertTo-EvidenceTimestamp -Value ([string]$existingRecord.generatedAt)
        return [DateTimeOffset]::Parse($existingGeneratedAt) -ge $incomingCreated
    }
    catch {
        return $true
    }
}

function Invoke-Publication {
    param(
        [object] $RecordOverride,
        [switch] $UseRecordOverride,
        [ValidateSet('success', 'failure')]
        [string] $CheckConclusion
    )

    if ($PrNumber -le 0) {
        throw 'PrNumber must be a positive issue number.'
    }

    $repositoryName = Get-RepositoryName -Override $Repository
    $record = if ($UseRecordOverride) {
        $RecordOverride
    }
    else {
        Get-PublicationRecord
    }
    $pr = Get-PrMetadata -RepositoryName $repositoryName -Number $PrNumber
    if ($EvidenceKind -eq 'pr-head-automated' -and $pr.HeadRefOid -ne $record.sourceSha) {
        throw "PR head SHA '$($pr.HeadRefOid)' does not match evidence SHA '$($record.sourceSha)'."
    }

    $targetType = if ($EvidenceKind -eq 'pr-head-automated') { 'pr' } else { 'issue' }
    $targetNumber = if ($targetType -eq 'pr') { $PrNumber } else { $pr.EvidenceIssue }
    $comments = Get-TargetComments -RepositoryName $repositoryName -TargetNumber $targetNumber
    $marker = "<!-- codex-evidence:$EvidenceKind -->"
    $markerComments = @($comments | Where-Object { [string]$_.body -like "*$marker*" })
    if ($markerComments.Count -gt 1) {
        throw "Target has multiple marker comments for evidence kind '$EvidenceKind'."
    }

    $commentBody = New-EvidenceCommentBody -Record $record
    $existingCommentId = $null
    $action = 'create'
    if ($markerComments.Count -eq 1) {
        $existing = $markerComments[0]
        $existingCommentId = [long]$existing.id
        if ([string]$existing.body -eq $commentBody) {
            $action = 'unchanged'
        }
        else {
            $existingRecord = Get-CommentEvidenceRecord -Body ([string]$existing.body)
            if ([string]$existingRecord.evidenceKind -ne $EvidenceKind) {
                throw 'Existing marker comment has the wrong evidence kind.'
            }
            Assert-PublicationFreshness -ExistingRecord $existingRecord -IncomingRecord $record
            $action = 'update'
        }
    }

    $preview = [ordered]@{
        schemaVersion = 1
        repository = $repositoryName
        evidenceKind = $EvidenceKind
        target = [ordered]@{
            type = $targetType
            number = $targetNumber
        }
        action = $action
        commentId = $existingCommentId
        marker = $marker
        record = $record
        commentBody = $commentBody
    }
    Write-JsonFile -Path $publicationPreviewPath -Value $preview
    Write-Host "Publication preview: $publicationPreviewPath"

    if (-not $Publish) {
        Write-Host ($preview | ConvertTo-Json -Depth 8)
        return 0
    }

    if ($action -eq 'unchanged') {
        Write-Host "Publication unchanged: comment $existingCommentId"
        Write-JsonFile -Path $publicationResultPath -Value ([ordered]@{
            status = 'UNCHANGED'
            action = $action
            commentId = $existingCommentId
            target = $preview.target
        })
        if ($UseRecordOverride -and -not [string]::IsNullOrWhiteSpace($CheckConclusion)) {
            Set-PublicationCheck -RepositoryName $repositoryName -Record $record -Conclusion $CheckConclusion
        }
        return 0
    }

    $arguments = if ($action -eq 'create') {
        @(
            'api'
            "repos/$repositoryName/issues/$targetNumber/comments"
            '--method'
            'POST'
            '--field'
            "body=$commentBody"
        )
    }
    else {
        @(
            'api'
            "repos/$repositoryName/issues/comments/$existingCommentId"
            '--method'
            'PATCH'
            '--field'
            "body=$commentBody"
        )
    }
    $result = Invoke-NativeCommand -Command 'gh' -Arguments $arguments
    if ($result.ExitCode -ne 0) {
        throw "gh api publication failed with exit code $($result.ExitCode)."
    }

    try {
        $response = (($result.Output | ForEach-Object { $_.ToString() }) -join "`n") | ConvertFrom-Json
    }
    catch {
        throw 'Published comment response is malformed.'
    }

    $resultRecord = [ordered]@{
        status = 'PUBLISHED'
        action = $action
        commentId = [long]$response.id
        target = $preview.target
    }
    Write-JsonFile -Path $publicationResultPath -Value $resultRecord
    Write-Host "Publication result: $publicationResultPath"
    if ($UseRecordOverride -and -not [string]::IsNullOrWhiteSpace($CheckConclusion)) {
        Set-PublicationCheck -RepositoryName $repositoryName -Record $record -Conclusion $CheckConclusion
    }
    return 0
}

function Set-PublicationCheck {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryName,
        [Parameter(Mandatory)]
        [object] $Record,
        [Parameter(Mandatory)]
        [ValidateSet('success', 'failure')]
        [string] $Conclusion
    )

    $summary = "Automated: $($Record.automatedStatus); Runtime: $($Record.runtimeStatus); Overall: $($Record.overallStatus)."
    $title = if ($Conclusion -eq 'success') {
        'Trusted verification evidence passed'
    }
    else {
        'Trusted verification evidence failed'
    }
    Set-CommitCheck -RepositoryName $RepositoryName -Sha $Record.sourceSha `
        -Conclusion $Conclusion -Title $title -Summary $summary
}

function Invoke-PrEvidencePublication {
    if ($EvidenceKind -ne 'pr-head-automated') {
        throw 'publish-pr requires pr-head-automated evidence.'
    }

    $repositoryName = $null
    $authoritativeSource = $null
    try {
        $repositoryName = Get-RepositoryName -Override $Repository
        $run = Get-GitHubWorkflowRun -RepositoryName $repositoryName
        $authoritativeSource = $run.SourceSha
        if ($run.Conclusion -notin @('success', 'failure')) {
            throw "Unsupported verification run conclusion: '$($run.Conclusion)'."
        }

        $pr = Get-PrMetadata -RepositoryName $repositoryName -Number $run.PrNumber
        if ($pr.State -ne 'OPEN') {
            throw "Pull request state '$($pr.State)' is not open."
        }
        if ($pr.BaseRefName -ne 'dev') {
            throw "Pull request targets '$($pr.BaseRefName)', not dev."
        }
        if ($pr.HeadRefOid -ne $run.SourceSha) {
            throw "PR head SHA '$($pr.HeadRefOid)' does not match authoritative run SHA '$($run.SourceSha)'."
        }
        Assert-EvidenceIssue -RepositoryName $repositoryName -Number $pr.EvidenceIssue

        $artifact = Assert-WorkflowArtifact -RepositoryName $repositoryName -Run $run
        $transport = Read-PrTransportRecord -RepositoryName $repositoryName -Run $run
        $record = Get-PublicationRecord -AutomatedEvidence $transport.evidence -UseSuppliedEvidence
        $record.prNumber = $run.PrNumber
        $record.evidenceIssue = $pr.EvidenceIssue
        $record.workflowRunId = $run.Id
        $record.workflowRunAttempt = $run.RunAttempt
        $record.workflowRunCreatedAt = $run.CreatedAt
        $record.workflowConclusion = $run.Conclusion
        $record.transportArtifactId = $artifact.Id
        $record.transportArtifactName = $artifact.Name
        $record.transportArtifactSize = $artifact.Size

        $PrNumber = $run.PrNumber
        $checkConclusion = if ($record.automatedStatus -eq 'PASS') {
            'success'
        }
        else {
            'failure'
        }
        Invoke-Publication -RecordOverride $record -UseRecordOverride -CheckConclusion $checkConclusion
        return 0
    }
    catch {
        $message = $_.Exception.Message
        if (
            -not [string]::IsNullOrWhiteSpace($repositoryName) -and
            $authoritativeSource -match '^[0-9a-f]{40}$' -and
            $message -notmatch '(?i)\bstale\b' -and
            -not (Test-NewerPublishedEvidence -RepositoryName $repositoryName -Run $run)
        ) {
            try {
                Set-CommitCheck -RepositoryName $repositoryName -Sha $authoritativeSource -Conclusion failure `
                    -Title 'Trusted verification evidence failed' `
                    -Summary "Publication failed: $message"
            }
            catch {
                $message = "$message Failure check also failed: $($_.Exception.Message)"
            }
        }
        throw $message
    }
}

function Invoke-AutomatedVerification {
$exitCode = 1
$script:evidence.generatedAt = Get-EvidenceTimestamp -Value $GeneratedAt
Write-Evidence
Push-Location $repoRoot
try {
    $script:evidence.sourceSha = Get-SourceSha
    Assert-CleanSourceTree -Phase 'at start'
    $script:evidence.buildProfileDigest = Get-BuildProfileDigest
    Test-WhitespaceInputs

    $sdkResult = Invoke-NativeCommand -Command 'dotnet' -Arguments @('--version')
    if ($sdkResult.ExitCode -ne 0) {
        throw "dotnet --version failed with exit code $($sdkResult.ExitCode)."
    }
    $sdkVersion = (($sdkResult.Output | ForEach-Object { $_.ToString() }) -join '').Trim()
    if ($sdkVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
        throw "dotnet --version returned a malformed version: '$sdkVersion'."
    }
    $script:evidence.sdkVersion = $sdkVersion

    $buildResult = Invoke-NativeCommand -Command 'dotnet' -Arguments @(
        'build'
        $productionProject
        '--configuration'
        'Release'
        "-p:SolutionDir=$repoRoot$([IO.Path]::DirectorySeparatorChar)"
        '-p:DisableDevelopmentDeployment=true'
    )
    if ($buildResult.ExitCode -ne 0) {
        throw "Production build failed with exit code $($buildResult.ExitCode)."
    }
    if (-not (Test-Path -LiteralPath $buildDll -PathType Leaf)) {
        throw "Production build did not produce the expected DLL: $buildDll"
    }
    $script:evidence.buildDllSha256 = (Get-FileHash -LiteralPath $buildDll -Algorithm SHA256).Hash.ToLowerInvariant()

    New-Item -ItemType Directory -Force -Path $testResultsDirectory | Out-Null
    $trxName = "automated-tests-$([guid]::NewGuid().ToString('N')).trx"
    $trxPath = Join-Path $testResultsDirectory $trxName
    $testResult = Invoke-NativeCommand -Command 'dotnet' -Arguments @(
        'test'
        $testProject
        '--configuration'
        'Release'
        '-p:DisableDevelopmentDeployment=true'
        '--logger'
        "trx;LogFileName=$trxName"
        '--results-directory'
        $testResultsDirectory
    )

    try {
        $counts = Read-TestCounts -TrxPath $trxPath
        $script:evidence.tests.executed = $counts.Executed
        $script:evidence.tests.passed = $counts.Passed
        $script:evidence.tests.failed = $counts.Failed
        $script:evidence.tests.skipped = $counts.Skipped
        $script:evidence.tests.status = if (
            $testResult.ExitCode -eq 0 -and
            $counts.Executed -gt 0 -and
            $counts.Failed -eq 0 -and
            $counts.Passed -eq $counts.Executed
        ) { 'PASS' } else { 'FAIL' }
    }
    catch {
        $script:evidence.tests.status = 'FAIL'
        throw
    }

    if ($testResult.ExitCode -ne 0) {
        throw "dotnet test failed with exit code $($testResult.ExitCode)."
    }
    if ($counts.Executed -eq 0) {
        throw 'dotnet test executed zero tests.'
    }
    if ($counts.Failed -ne 0 -or $counts.Passed -ne $counts.Executed) {
        throw 'dotnet test reported unsuccessful test results.'
    }

    $postBuildSha = Get-SourceSha
    if ($postBuildSha -ne $script:evidence.sourceSha) {
        throw "Source SHA changed during verification: $($script:evidence.sourceSha) -> $postBuildSha."
    }
    Assert-CleanSourceTree -Phase 'before PASS'

    $script:evidence.automatedStatus = 'PASS'
    $exitCode = 0
}
catch {
    $script:errorMessages.Add($_.Exception.Message)
    $script:evidence.automatedStatus = 'FAIL'
}
finally {
    Pop-Location
    Write-Evidence
    Write-Host "Evidence: $evidencePath"
}

return $exitCode
}

$exitCode = 1
try {
    $exitCode = switch ($Mode) {
        'automated' { Invoke-AutomatedVerification }
        'smoke' { Invoke-SmokeVerification }
        'publish' { Invoke-Publication }
        'transport' { Invoke-TransportCreation }
        'publish-pr' { Invoke-PrEvidencePublication }
    }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}

exit $exitCode
