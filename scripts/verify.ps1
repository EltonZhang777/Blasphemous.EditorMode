#Requires -Version 7.2

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$productionProject = Join-Path $repoRoot 'Blasphemous.EditorMode\Blasphemous.EditorMode.csproj'
$testProject = Join-Path $repoRoot 'tests\Blasphemous.EditorMode.Tests\Blasphemous.EditorMode.Tests.csproj'
$testProjectRelative = 'tests/Blasphemous.EditorMode.Tests/Blasphemous.EditorMode.Tests.csproj'
$buildDll = Join-Path $repoRoot 'Blasphemous.EditorMode\bin\Release\EditorMode.dll'
$evidencePath = Join-Path $repoRoot 'artifacts\automated-evidence.json'
$testResultsDirectory = Join-Path $repoRoot 'artifacts\test-results'

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

$exitCode = 1
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

exit $exitCode
