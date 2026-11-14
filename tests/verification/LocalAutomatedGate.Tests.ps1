BeforeAll {
    $script:sourceRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $script:gateSource = Join-Path $script:sourceRepoRoot 'scripts\verify.ps1'
    $script:pwshPath = (Get-Process -Id $PID).Path
    $script:sourceSha = '0123456789abcdef0123456789abcdef01234567'

    function New-FixtureTextFile {
        param(
            [Parameter(Mandatory)]
            [string] $Path,
            [string] $Content = 'fixture',
            [bool] $FinalNewline = $true
        )

        $parent = Split-Path -Parent $Path
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
        $text = if ($FinalNewline) { $Content + "`n" } else { $Content }
        [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
    }

    function New-GateFixture {
        param(
            [bool] $WhitespaceFailure = $false,
            [bool] $GameSentinel = $false
        )

        $fixtureRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $repo = Join-Path $fixtureRoot 'repo'
        $bin = Join-Path $repo 'bin'
        $game = Join-Path $fixtureRoot 'game'
        New-Item -ItemType Directory -Force -Path (Join-Path $repo 'scripts'), $bin, $game | Out-Null
        Copy-Item -LiteralPath $script:gateSource -Destination (Join-Path $repo 'scripts\verify.ps1')

        New-FixtureTextFile -Path (Join-Path $repo 'NuGet.Config') -Content '<configuration />'
        New-FixtureTextFile -Path (Join-Path $repo 'Blasphemous.EditorMode\Blasphemous.EditorMode.csproj') -Content '<Project />'
        New-FixtureTextFile -Path (Join-Path $repo 'Blasphemous.EditorMode\NuGet.Config') -Content '<configuration />'
        New-FixtureTextFile -Path (Join-Path $repo 'Blasphemous.EditorMode\packages.lock.json') -Content '{}'
        New-FixtureTextFile -Path (Join-Path $repo 'Blasphemous.EditorMode\Source.cs') -FinalNewline (-not $WhitespaceFailure) -Content 'internal sealed class Source { }'
        New-FixtureTextFile -Path (Join-Path $repo 'tests\Blasphemous.EditorMode.Tests\Blasphemous.EditorMode.Tests.csproj') -Content '<Project />'
        New-FixtureTextFile -Path (Join-Path $repo 'tests\Blasphemous.EditorMode.Tests\packages.lock.json') -Content '{}'
        if ($WhitespaceFailure) {
            New-FixtureTextFile -Path (Join-Path $repo 'tests\Blasphemous.EditorMode.Tests\SourceTests.cs') -Content 'internal sealed class SourceTests { }   '
        }
        else {
            New-FixtureTextFile -Path (Join-Path $repo 'tests\Blasphemous.EditorMode.Tests\SourceTests.cs') -Content 'internal sealed class SourceTests { }'
        }

        if ($GameSentinel) {
            New-FixtureTextFile -Path (Join-Path $game 'sentinel.txt') -Content 'unchanged'
        }

        @'
param()
$ErrorActionPreference = 'Stop'

if ($env:FAKE_DOTNET_LOG) {
    Add-Content -LiteralPath $env:FAKE_DOTNET_LOG -Value ($args -join ' ')
}

if ($args.Count -eq 1 -and $args[0] -eq '--version') {
    [Console]::Out.WriteLine($env:FAKE_SDK_VERSION)
    exit 0
}

if ($args[0] -in @('build', 'test') -and ($args -join ' ') -notmatch 'DisableDevelopmentDeployment=true' -and $env:FAKE_GAME_DIR) {
    $plugins = Join-Path $env:FAKE_GAME_DIR 'plugins'
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    [IO.File]::WriteAllText((Join-Path $plugins 'EditorMode.dll'), 'deployed')
}

switch ($args[0]) {
    'build' {
        $dll = Join-Path (Get-Location) 'Blasphemous.EditorMode\bin\Release\EditorMode.dll'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dll) | Out-Null
        [IO.File]::WriteAllBytes($dll, [Text.Encoding]::UTF8.GetBytes('fixture-dll'))
        exit 0
    }
    'test' {
        $resultsDirectory = $null
        $logName = 'results.trx'
        for ($index = 0; $index -lt $args.Count; $index++) {
            if ($args[$index] -eq '--results-directory') {
                $resultsDirectory = $args[$index + 1]
            }
            elseif ($args[$index] -like 'trx;LogFileName=*') {
                $logName = $args[$index].Substring('trx;LogFileName='.Length)
            }
        }

        if (-not $resultsDirectory) {
            throw 'Missing --results-directory.'
        }

        New-Item -ItemType Directory -Force -Path $resultsDirectory | Out-Null
        $mode = $env:FAKE_TEST_MODE
        $rejectedCounter = $env:FAKE_REJECTED_COUNTER
        $completed = 0
        $inProgress = 0
        $skipResultCount = 0
        if ($mode -eq 'zero') {
            $total = 0
            $executed = 0
            $passed = 0
            $failed = 0
            $notExecuted = 0
            $outcome = 'Completed'
            $exitCode = 0
        }
        elseif ($mode -eq 'fail') {
            $total = 5
            $executed = 5
            $passed = 4
            $failed = 1
            $notExecuted = 0
            $outcome = 'Completed'
            $exitCode = 1
        }
        elseif ($mode -eq 'not-completed') {
            $total = 5
            $executed = 5
            $passed = 5
            $failed = 0
            $notExecuted = 0
            $outcome = 'Failed'
            $exitCode = 0
        }
        elseif ($mode -eq 'in-progress') {
            $total = 6
            $executed = 5
            $passed = 5
            $failed = 0
            $notExecuted = 0
            $inProgress = 1
            $outcome = 'Completed'
            $exitCode = 0
        }
        elseif ($mode -eq 'incomplete-total') {
            $total = 6
            $executed = 5
            $passed = 5
            $failed = 0
            $notExecuted = 0
            $outcome = 'Completed'
            $exitCode = 0
        }
        elseif ($mode -in @('pass-no-skip', 'pass-counter-skip')) {
            $total = 5
            $executed = 5
            $passed = 5
            $failed = 0
            $notExecuted = 0
            if ($mode -eq 'pass-counter-skip') {
                $total = 6
                $notExecuted = 1
                $skipResultCount = 1
            }
            $outcome = 'Completed'
            $exitCode = 0
        }
        elseif ($mode -in @('duplicate-summary', 'duplicate-counters', 'hidden-failed', 'completed', 'inconsistent-executed', 'total-too-small', 'result-count-mismatch', 'counter-only-skipped', 'misplaced-summary', 'misplaced-counters', 'nested-result', 'outside-result')) {
            $total = 5
            $executed = 5
            $passed = 5
            $failed = 0
            $notExecuted = 0
            $outcome = 'Completed'
            $exitCode = 0
        }
        else {
            $total = 6
            $executed = 5
            $passed = 5
            $failed = 0
            $notExecuted = 0
            $skipResultCount = 1
            $outcome = 'Completed'
            $exitCode = 0
        }

        if ($mode -eq 'completed') {
            $completed = 1
        }
        elseif ($mode -eq 'inconsistent-executed') {
            $passed = 4
        }
        elseif ($mode -eq 'total-too-small') {
            $total = 4
        }
        elseif ($mode -eq 'counter-only-skipped') {
            $total = 6
            $notExecuted = 1
        }

        $resultOutcomes = @(
            for ($index = 0; $index -lt $passed; $index++) {
                'Passed'
            }
            for ($index = 0; $index -lt $failed; $index++) {
                'Failed'
            }
            for ($index = 0; $index -lt $skipResultCount; $index++) {
                'NotExecuted'
            }
        )
        if ($mode -eq 'hidden-failed') {
            $resultOutcomes = @($resultOutcomes | Select-Object -SkipLast 1) + 'Failed'
        }
        elseif ($mode -eq 'result-count-mismatch') {
            $resultOutcomes = @($resultOutcomes | Select-Object -SkipLast 1)
        }
        $unitResultLines = @(
            for ($index = 0; $index -lt $resultOutcomes.Count; $index++) {
                '    <UnitTestResult testId="fixture-{0}" testName="fixture-{0}" outcome="{1}" />' -f ($index + 1), $resultOutcomes[$index]
            }
        )
        $unitResultsXml = $unitResultLines -join "`n"
        $counterValues = [ordered]@{
            total = $total
            executed = $executed
            passed = $passed
            failed = $failed
            error = 0
            timeout = 0
            aborted = 0
            inconclusive = 0
            passedButRunAborted = 0
            notRunnable = 0
            notExecuted = $notExecuted
            disconnected = 0
            warning = 0
            completed = $completed
            inProgress = $inProgress
            pending = 0
        }
        if ($rejectedCounter) {
            if (-not $counterValues.Contains($rejectedCounter)) {
                throw "Unknown rejected counter: $rejectedCounter"
            }
            $counterValues[$rejectedCounter] = 1
        }
        $counterAttributes = @(
            $counterValues.GetEnumerator() | ForEach-Object {
                '{0}="{1}"' -f $_.Key, $_.Value
            }
        ) -join ' '
        $countersXml = "  <Counters $counterAttributes />"
        if ($mode -eq 'duplicate-counters') {
            $countersXml = $countersXml + "`n" + $countersXml
        }
        $summaryXml = "  <ResultSummary outcome=`"$outcome`">`n$countersXml`n  </ResultSummary>"
        if ($mode -eq 'duplicate-summary') {
            $summaryXml = $summaryXml + "`n" + $summaryXml
        }
        $misplacedXml = if ($mode -eq 'misplaced-summary') {
            "  <ResultSummary outcome=`"Completed`">`n$countersXml`n  </ResultSummary>"
        }
        elseif ($mode -eq 'misplaced-counters') {
            $countersXml
        }
        else {
            ''
        }
        $nestedResultsXml = if ($mode -eq 'nested-result') {
            '    <InnerResults><UnitTestResult testId="nested" testName="nested" outcome="Failed" /></InnerResults>'
        }
        else {
            ''
        }
        $outsideResultXml = if ($mode -eq 'outside-result') {
            '  <UnitTestResult testId="outside" testName="outside" outcome="Failed" />'
        }
        else {
            ''
        }

        $trx = @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
$summaryXml
  <Results>
$unitResultsXml
$misplacedXml
$nestedResultsXml
  </Results>
$outsideResultXml
</TestRun>
"@
        [IO.File]::WriteAllText((Join-Path $resultsDirectory $logName), $trx, [Text.UTF8Encoding]::new($false))
        exit $exitCode
    }
    default {
        exit 3
    }
}
'@ | Set-Content -LiteralPath (Join-Path $bin 'fake-dotnet.ps1') -Encoding utf8NoBOM

        @"
@echo off
"$script:pwshPath" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0fake-dotnet.ps1" %*
exit /b %errorlevel%
"@ | Set-Content -LiteralPath (Join-Path $bin 'dotnet.cmd') -Encoding ascii

        @'
@echo off
if "%~1"=="rev-parse" if "%~2"=="HEAD" (
    echo %FAKE_GIT_SHA%
    exit /b 0
)
if "%~1"=="status" (
    if defined FAKE_GIT_STATUS_MARKER (
        if exist "%FAKE_GIT_STATUS_MARKER%" (
            echo %FAKE_GIT_STATUS_AFTER%
        ) else (
            type nul > "%FAKE_GIT_STATUS_MARKER%"
            if defined FAKE_GIT_STATUS echo %FAKE_GIT_STATUS%
        )
        exit /b 0
    )
    if defined FAKE_GIT_STATUS echo %FAKE_GIT_STATUS%
    exit /b 0
)
exit /b 1
'@ | Set-Content -LiteralPath (Join-Path $bin 'git.cmd') -Encoding ascii

        [pscustomobject]@{
            Root = $fixtureRoot
            Repo = $repo
            Bin = $bin
            Game = $game
            Evidence = Join-Path $repo 'artifacts\automated-evidence.json'
            DotNetLog = Join-Path $fixtureRoot 'dotnet.log'
        }
    }

    function Invoke-FixtureGate {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture,
            [hashtable] $Environment = @{}
        )

        $originalPath = $env:PATH
        $originalValues = @{}
        $allEnvironment = @{
            FAKE_SDK_VERSION = '10.0.401'
            FAKE_GIT_SHA = $script:sourceSha
            FAKE_GIT_STATUS = ''
            FAKE_GAME_DIR = $Fixture.Game
            FAKE_DOTNET_LOG = $Fixture.DotNetLog
            FAKE_TEST_MODE = 'pass'
        }
        foreach ($key in $allEnvironment.Keys) {
            $originalValues[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, [string]$allEnvironment[$key], 'Process')
        }
        foreach ($key in $Environment.Keys) {
            if (-not $originalValues.ContainsKey($key)) {
                $originalValues[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            }
            [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], 'Process')
        }

        try {
            $env:PATH = "$($Fixture.Bin);$originalPath"
            Push-Location $Fixture.Repo
            try {
                $output = & $script:pwshPath -NoProfile -NonInteractive -File (Join-Path $Fixture.Repo 'scripts\verify.ps1') 2>&1
                $exitCode = $LASTEXITCODE
            }
            finally {
                Pop-Location
            }
        }
        finally {
            $env:PATH = $originalPath
            foreach ($key in $originalValues.Keys) {
                [Environment]::SetEnvironmentVariable($key, $originalValues[$key], 'Process')
            }
        }

        [pscustomobject]@{
            ExitCode = $exitCode
            Output = ($output | ForEach-Object { $_.ToString() }) -join "`n"
        }
    }

    function Read-FixtureEvidence {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture
        )

        Get-Content -Raw -LiteralPath $Fixture.Evidence | ConvertFrom-Json
    }
}

Describe 'local automated verification gate' {
    It 'passes with canonical evidence when build, tests, and whitespace succeed' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $evidence.automatedStatus | Should -Be 'PASS'
        $evidence.sourceSha | Should -Be $script:sourceSha
        $evidence.sourceTreeStatus | Should -Be 'clean'
        $evidence.buildDllSha256 | Should -Be (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $fixture.Repo 'Blasphemous.EditorMode\bin\Release\EditorMode.dll')).Hash.ToLowerInvariant()
        $evidence.buildProfileDigest | Should -Match '^[0-9a-f]{64}$'
        $evidence.tests.project | Should -Be 'tests/Blasphemous.EditorMode.Tests/Blasphemous.EditorMode.Tests.csproj'
        $evidence.tests.executed | Should -Be 5
        $evidence.tests.passed | Should -Be 5
        $evidence.tests.failed | Should -Be 0
        $evidence.tests.skipped | Should -Be 1
        $evidence.whitespace.status | Should -Be 'PASS'
        $evidence.sdkVersion | Should -Be '10.0.401'
        $evidence.runner.os | Should -Be 'Windows'
        $evidence.runner.machineName | Should -Not -BeNullOrEmpty
    }

    It 'computes the same build-profile digest for LF and CRLF checkouts' {
        $lfFixture = New-GateFixture
        $crlfFixture = New-GateFixture
        $relativeProject = 'Blasphemous.EditorMode\Blasphemous.EditorMode.csproj'
        [IO.File]::WriteAllText((Join-Path $lfFixture.Repo $relativeProject), "<Project>`n</Project>`n", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $crlfFixture.Repo $relativeProject), "<Project>`r`n</Project>`r`n", [Text.UTF8Encoding]::new($false))

        $lfResult = Invoke-FixtureGate -Fixture $lfFixture
        $crlfResult = Invoke-FixtureGate -Fixture $crlfFixture
        $lfEvidence = Read-FixtureEvidence -Fixture $lfFixture
        $crlfEvidence = Read-FixtureEvidence -Fixture $crlfFixture

        $lfResult.ExitCode | Should -Be 0 -Because $lfResult.Output
        $crlfResult.ExitCode | Should -Be 0 -Because $crlfResult.Output
        $lfEvidence.buildProfileDigest | Should -Match '^[0-9a-f]{64}$'
        $crlfEvidence.buildProfileDigest | Should -Be $lfEvidence.buildProfileDigest
    }

    It 'passes without skipped tests' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'pass-no-skip' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $evidence.tests.executed | Should -Be 5
        $evidence.tests.passed | Should -Be 5
        $evidence.tests.skipped | Should -Be 0
    }

    It 'passes when skipped counters match skipped result entries' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'pass-counter-skip' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $evidence.tests.executed | Should -Be 5
        $evidence.tests.passed | Should -Be 5
        $evidence.tests.skipped | Should -Be 1
    }

    It 'returns non-zero and FAIL evidence when a test fails' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'fail' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        $evidence.tests.status | Should -Be 'FAIL'
        $evidence.tests.executed | Should -Be 5
        $evidence.tests.passed | Should -Be 4
        $evidence.tests.failed | Should -Be 1
        $evidence.tests.skipped | Should -Be 0
    }

    It 'rejects zero executed tests even when dotnet exits zero' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'zero' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        $evidence.tests.status | Should -Be 'FAIL'
        $evidence.tests.executed | Should -Be 0
    }

    It 'rejects a non-Completed TRX summary even when dotnet exits zero' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'not-completed' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        $evidence.tests.status | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match 'expected ''Completed'''
    }

    It 'rejects in-progress tests even when dotnet exits zero' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'in-progress' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        $evidence.tests.status | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match 'inProgress'
    }

    It 'rejects a total that exceeds executed plus skipped tests' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'incomplete-total' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        $evidence.tests.status | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match 'inconsistent'
    }

    It 'rejects duplicate result summaries' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'duplicate-summary' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match 'exactly one summary'
    }

    It 'rejects duplicate counter elements' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'duplicate-counters' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match 'exactly one counters'
    }

    It 'rejects misplaced <Kind>' -TestCases @(
        @{ Kind = 'summary'; Mode = 'misplaced-summary'; Error = 'exactly one summary' }
        @{ Kind = 'counters'; Mode = 'misplaced-counters'; Error = 'exactly one counters' }
    ) {
        param($Kind, $Mode, $Error)

        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = $Mode }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match ([regex]::Escape($Error))
    }

    It 'rejects a <Kind> UnitTestResult' -TestCases @(
        @{ Kind = 'nested'; Mode = 'nested-result' }
        @{ Kind = 'misplaced'; Mode = 'outside-result' }
    ) {
        param($Kind, $Mode)

        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = $Mode }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match 'one Results element and no misplaced UnitTestResult'
    }

    It 'rejects a failed unit result hidden by passing counters' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = 'hidden-failed' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match 'outcomes are inconsistent'
    }

    It 'rejects <Case> TRX counters and result entries' -TestCases @(
        @{ Case = 'completed counter'; Mode = 'completed'; Error = "counter 'completed'" }
        @{ Case = 'executed mismatch'; Mode = 'inconsistent-executed'; Error = 'counters are inconsistent' }
        @{ Case = 'undersized total'; Mode = 'total-too-small'; Error = 'counters are inconsistent' }
        @{ Case = 'missing unit result'; Mode = 'result-count-mismatch'; Error = 'do not match executed plus skipped count' }
        @{ Case = 'counter-only skipped'; Mode = 'counter-only-skipped'; Error = 'Skipped test counters are inconsistent' }
    ) {
        param($Case, $Mode, $Error)

        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_TEST_MODE = $Mode }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match ([regex]::Escape($Error))
    }

    It 'rejects a nonzero <Counter> counter' -TestCases @(
        @{ Counter = 'error' }
        @{ Counter = 'timeout' }
        @{ Counter = 'aborted' }
        @{ Counter = 'inconclusive' }
        @{ Counter = 'passedButRunAborted' }
        @{ Counter = 'pending' }
        @{ Counter = 'warning' }
        @{ Counter = 'disconnected' }
        @{ Counter = 'completed' }
    ) {
        param($Counter)

        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_REJECTED_COUNTER = $Counter }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match ([regex]::Escape("counter '$Counter'"))
    }

    It 'rejects a dirty source tree and replaces stale PASS evidence' {
        $fixture = New-GateFixture
        New-FixtureTextFile -Path $fixture.Evidence -Content '{"automatedStatus":"PASS"}'
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{ FAKE_GIT_STATUS = '?? dirty.txt' }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        $evidence.sourceTreeStatus | Should -Be 'dirty'
        $evidence.buildDllSha256 | Should -BeNullOrEmpty
        $evidence.tests.status | Should -Be 'NOT_RUN'
        $evidence.whitespace.status | Should -Be 'NOT_RUN'
        (Test-Path -LiteralPath $fixture.DotNetLog) | Should -BeFalse
        ($evidence.errors -join "`n") | Should -Match 'Source tree is dirty'
    }

    It 'rejects a source tree that becomes dirty before PASS' {
        $fixture = New-GateFixture
        $result = Invoke-FixtureGate -Fixture $fixture -Environment @{
            FAKE_GIT_STATUS_MARKER = (Join-Path $fixture.Root 'first-status-checked.txt')
            FAKE_GIT_STATUS_AFTER = '?? late.txt'
        }
        $evidence = Read-FixtureEvidence -Fixture $fixture

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        $evidence.sourceTreeStatus | Should -Be 'dirty'
        $evidence.tests.status | Should -Be 'PASS'
        ($evidence.errors -join "`n") | Should -Match 'Source tree is dirty before PASS'
    }

    It 'checks both production and test inputs for whitespace' {
        $fixture = New-GateFixture -WhitespaceFailure $true
        $result = Invoke-FixtureGate -Fixture $fixture
        $evidence = Read-FixtureEvidence -Fixture $fixture
        $findingPaths = @($evidence.whitespace.findings | ForEach-Object path)

        $result.ExitCode | Should -Not -Be 0
        $evidence.automatedStatus | Should -Be 'FAIL'
        $evidence.whitespace.status | Should -Be 'FAIL'
        $findingPaths | Should -Contain 'Blasphemous.EditorMode/Source.cs'
        $findingPaths | Should -Contain 'tests/Blasphemous.EditorMode.Tests/SourceTests.cs'
    }

    It 'does not deploy during the default run' {
        $fixture = New-GateFixture -GameSentinel $true
        $result = Invoke-FixtureGate -Fixture $fixture
        $gameFiles = @(Get-ChildItem -LiteralPath $fixture.Game -Recurse -File | ForEach-Object Name)
        $buildLog = @(Get-Content -LiteralPath $fixture.DotNetLog | Where-Object { $_ -like 'build *' })
        $testLog = @(Get-Content -LiteralPath $fixture.DotNetLog | Where-Object { $_ -like 'test *' })

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $buildLog.Count | Should -Be 1
        $buildLog[0] | Should -Match 'DisableDevelopmentDeployment=true'
        $buildLog[0].Contains('Blasphemous.EditorMode\Blasphemous.EditorMode.csproj') | Should -BeTrue
        $buildLog[0].Contains('--configuration Release') | Should -BeTrue
        $testLog.Count | Should -Be 1
        $testLog[0] | Should -Match 'DisableDevelopmentDeployment=true'
        $testLog[0].Contains('tests\Blasphemous.EditorMode.Tests\Blasphemous.EditorMode.Tests.csproj') | Should -BeTrue
        $testLog[0].Contains('--logger') | Should -BeTrue
        $testLog[0].Contains('--results-directory') | Should -BeTrue
        $gameFiles | Should -Be @('sentinel.txt')
    }

    It 'guards both development deployment targets with the disable switch' {
        $projectPath = Join-Path $script:sourceRepoRoot 'Blasphemous.EditorMode\Blasphemous.EditorMode.csproj'
        [xml] $project = Get-Content -Raw -LiteralPath $projectPath
        $developmentTarget = $project.SelectSingleNode("//*[local-name()='Target' and @Name='Development']")
        $developmentCopies = @(
            $developmentTarget.SelectNodes("./*[local-name()='Copy']") |
                Where-Object { $_.DestinationFolder.Contains('$(DevFolder)') }
        )

        $developmentCopies.Count | Should -Be 2
        foreach ($copy in $developmentCopies) {
            $copy.GetAttribute('Condition') | Should -Match 'DisableDevelopmentDeployment'
        }
    }

}
