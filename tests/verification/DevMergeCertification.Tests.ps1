BeforeAll {
    $script:sourceRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $script:gateSource = Join-Path $script:sourceRepoRoot 'scripts\verify.ps1'
    $script:pwshPath = (Get-Process -Id $PID).Path
    $script:finalSha = '1111111111111111111111111111111111111111'
    $script:reviewedSha = '2222222222222222222222222222222222222222'
    $script:generatedAt = [DateTimeOffset]::UtcNow.ToString('O')

    function Write-FixtureJson {
        param(
            [Parameter(Mandatory)][string] $Path,
            [Parameter(Mandatory)][object] $Value
        )

        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
        [IO.File]::WriteAllText(
            $Path,
            (($Value | ConvertTo-Json -Depth 12) + "`n"),
            [Text.UTF8Encoding]::new($false)
        )
    }

    function Read-FixtureJson {
        param([Parameter(Mandatory)][string] $Path)
        Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    }

    function New-Evidence {
        param([string] $Status = 'PASS')

        [ordered]@{
            schemaVersion = 1
            evidenceKind = 'automated'
            generatedAt = $script:generatedAt
            sourceSha = $script:finalSha
            sourceTreeStatus = 'clean'
            buildDllSha256 = ('a' * 64)
            buildProfileDigest = ('b' * 64)
            tests = [ordered]@{
                project = 'tests/Blasphemous.EditorMode.Tests/Blasphemous.EditorMode.Tests.csproj'
                status = $Status
                executed = 81
                passed = if ($Status -eq 'PASS') { 81 } else { 80 }
                failed = if ($Status -eq 'PASS') { 0 } else { 1 }
                skipped = 0
            }
            whitespace = [ordered]@{
                status = 'PASS'
                checkedFiles = 57
                findings = @()
            }
            automatedStatus = $Status
            sdkVersion = '10.0.401'
            runner = [ordered]@{
                os = 'Windows'
                osVersion = '10.0.26100'
                architecture = 'X64'
                powershellVersion = '7.5.0'
                machineName = 'fixture'
            }
            errors = @()
        }
    }

    function New-DevFixture {
        param([string] $Status = 'PASS')

        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $repo = Join-Path $root 'repo'
        $bin = Join-Path $repo 'bin'
        New-Item -ItemType Directory -Force -Path (Join-Path $repo 'scripts'), $bin, (Join-Path $repo 'artifacts') | Out-Null
        Copy-Item -LiteralPath $script:gateSource -Destination (Join-Path $repo 'scripts\verify.ps1')
        $evidencePath = Join-Path $repo 'artifacts\automated-evidence.json'
        Write-FixtureJson -Path $evidencePath -Value (New-Evidence -Status $Status)

        @'
param()

$command = $args -join ' '
if ($env:FAKE_DEV_GH_LOG) {
    Add-Content -LiteralPath $env:FAKE_DEV_GH_LOG -Value $command
}
if ($args.Count -ge 2 -and $args[0] -eq 'pr' -and $args[1] -eq 'view') {
    [Console]::Out.WriteLine($env:FAKE_DEV_PR_JSON)
    exit 0
}
if ($args[0] -ne 'api') {
    exit 2
}
if ($command -match 'git/ref/heads/dev' -and $command -notmatch '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_DEV_HEAD_JSON)
    exit 0
}
if ($command -match 'commits/[0-9a-f]+/pulls' -and $command -notmatch '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_DEV_PRS_JSON)
    exit 0
}
if ($command -match 'issues/\d+/comments' -and $command -notmatch '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_DEV_COMMENTS_JSON)
    exit 0
}
if ($command -match 'issues/\d+(\s|$)' -and $command -notmatch '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_DEV_ISSUE_JSON)
    exit 0
}
if ($command -match '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_DEV_WRITE_JSON)
    exit 0
}
exit 2
'@ | Set-Content -LiteralPath (Join-Path $bin 'fake-gh.ps1') -Encoding utf8NoBOM

        @"
@echo off
"$script:pwshPath" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0fake-gh.ps1" %*
exit /b %errorlevel%
"@ | Set-Content -LiteralPath (Join-Path $bin 'gh.cmd') -Encoding ascii
        New-Item -ItemType File -Force -Path (Join-Path $root 'gh.log') | Out-Null

        [pscustomobject]@{
            Root = $root
            Repo = $repo
            Bin = $bin
            Evidence = $evidencePath
            GhLog = Join-Path $root 'gh.log'
            Preview = Join-Path $repo 'artifacts\dev-publication-preview.json'
            Result = Join-Path $repo 'artifacts\dev-publication-result.json'
        }
    }

    function New-PullAssociationJson {
        param(
            [string] $HeadSha = $script:reviewedSha,
            [string] $MergedAt = '2026-11-14T09:55:00Z'
        )

        @([ordered]@{
            number = 42
            state = 'closed'
            merged_at = $MergedAt
            head = [ordered]@{ sha = $HeadSha }
            base = [ordered]@{ ref = 'dev' }
        }) | ConvertTo-Json -Depth 6 -Compress
    }

    function New-DevHeadJson {
        param([string] $Sha = $script:finalSha)

        [ordered]@{ object = [ordered]@{ sha = $Sha } } | ConvertTo-Json -Depth 4 -Compress
    }

    function New-PrJson {
        param(
            [string] $HeadSha = $script:reviewedSha,
            [string] $State = 'MERGED'
        )

        [ordered]@{
            body = 'Evidence issue: #7'
            headRefOid = $HeadSha
            baseRefName = 'dev'
            state = $State
            isDraft = $false
        } | ConvertTo-Json -Compress
    }

    function New-IssueJson {
        [ordered]@{ number = 7; state = 'open'; title = 'Evidence issue' } |
            ConvertTo-Json -Compress
    }

    function New-EvidenceComment {
        param([string] $GeneratedAt = ([DateTimeOffset]::Parse($script:generatedAt).AddMinutes(-5).ToString('O')))

        $record = [ordered]@{
            schemaVersion = 1
            evidenceKind = 'dev-automated'
            sourceSha = $script:finalSha
            mergedSha = $script:finalSha
            prHeadSha = $script:reviewedSha
            generatedAt = $GeneratedAt
            automatedStatus = 'PASS'
            runtimeStatus = 'PARTIAL'
            overallStatus = 'PARTIAL'
        }
        @(
            '<!-- codex-evidence:dev-automated -->'
            '## Verification evidence: dev-automated'
            ''
            '<!-- codex-evidence-json:start -->'
            ($record | ConvertTo-Json -Depth 6 -Compress)
            '<!-- codex-evidence-json:end -->'
        ) -join "`n"
    }

    function New-Environment {
        param(
            [Parameter(Mandatory)][pscustomobject] $Fixture,
            [string] $PrsJson = (New-PullAssociationJson),
            [string] $PrJson = (New-PrJson),
            [string] $CommentsJson = '[]',
            [string] $WriteJson = '{"id":501}',
            [string] $IssueJson = (New-IssueJson),
            [string] $DevHeadJson = (New-DevHeadJson)
        )

        @{
            FAKE_DEV_GH_LOG = $Fixture.GhLog
            FAKE_DEV_HEAD_JSON = $DevHeadJson
            FAKE_DEV_PRS_JSON = $PrsJson
            FAKE_DEV_PR_JSON = $PrJson
            FAKE_DEV_COMMENTS_JSON = $CommentsJson
            FAKE_DEV_ISSUE_JSON = $IssueJson
            FAKE_DEV_WRITE_JSON = $WriteJson
        }
    }

    function Invoke-DevScript {
        param(
            [Parameter(Mandatory)][pscustomobject] $Fixture,
            [hashtable] $Environment = @{}
        )

        $originalPath = $env:PATH
        $originalValues = @{}
        foreach ($key in $Environment.Keys) {
            $originalValues[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key], 'Process')
        }
        try {
            $env:PATH = "$($Fixture.Bin);$originalPath"
            $output = & $script:pwshPath -NoProfile -NonInteractive -File (Join-Path $Fixture.Repo 'scripts\verify.ps1') `
                -Mode publish-dev -SourceSha $script:finalSha -Repository 'owner/repo' `
                -WorkflowRunId 200 -WorkflowRunAttempt 1 -WorkflowName 'Dev verification' `
                -WorkflowPath '.github/workflows/verify-dev.yml' -WorkflowEvent push -Publish 2>&1
            $exitCode = $LASTEXITCODE
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

    function Assert-NoDevWrites {
        param([Parameter(Mandatory)][pscustomobject] $Fixture)

        $log = @(Get-Content -LiteralPath $Fixture.GhLog)
        @($log | Where-Object { $_ -match '--method (POST|PATCH)' }).Count | Should -Be 0
    }
}

Describe 'final dev certification' {
    It 'publishes final evidence linking the final and reviewed SHAs' {
        $fixture = New-DevFixture
        $result = Invoke-DevScript -Fixture $fixture -Environment (New-Environment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.Preview
        $published = Read-FixtureJson -Path $fixture.Result

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $preview.target.type | Should -Be 'issue'
        $preview.target.number | Should -Be 7
        $preview.record.evidenceKind | Should -Be 'dev-automated'
        $preview.record.sourceSha | Should -Be $script:finalSha
        $preview.record.mergedSha | Should -Be $script:finalSha
        $preview.record.prHeadSha | Should -Be $script:reviewedSha
        $preview.record.automatedStatus | Should -Be 'PASS'
        $preview.record.overallStatus | Should -Be 'PARTIAL'
        $preview.record.buildDllSha256 | Should -Be ('a' * 64)
        $preview.record.buildProfileDigest | Should -Be ('b' * 64)
        $preview.record.tests.executed | Should -Be 81
        $preview.record.whitespace.checkedFiles | Should -Be 57
        $preview.commentBody | Should -Match 'codex-evidence:dev-automated'
        $preview.commentBody | Should -Match $script:finalSha
        $preview.commentBody | Should -Match $script:reviewedSha
        $preview.commentBody | Should -Match "https://github.com/owner/repo/commit/$($script:finalSha)"
        $preview.commentBody | Should -Match "https://github.com/owner/repo/commit/$($script:reviewedSha)"
        $preview.commentBody | Should -Match 'https://github.com/owner/repo/issues/7'
        $preview.record.evidenceIssue | Should -Be 7
        $preview.record.mergedShaUrl | Should -Be "https://github.com/owner/repo/commit/$($script:finalSha)"
        $preview.record.prHeadShaUrl | Should -Be "https://github.com/owner/repo/commit/$($script:reviewedSha)"
        $preview.record.evidenceIssueUrl | Should -Be 'https://github.com/owner/repo/issues/7'
        $published.status | Should -Be 'PUBLISHED'
        $published.target.number | Should -Be 7
    }

    It 'marks a missing PR association as PARTIAL without writing issue evidence' {
        $fixture = New-DevFixture
        $result = Invoke-DevScript -Fixture $fixture -Environment (
            New-Environment -Fixture $fixture -PrsJson '[]'
        )
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.status | Should -Be 'PARTIAL'
        $preview.target.type | Should -Be 'none'
        $preview.record.overallStatus | Should -Be 'PARTIAL'
        Assert-NoDevWrites -Fixture $fixture
    }

    It 'keeps a failed gate FAIL while an unavailable association stays PARTIAL' {
        $fixture = New-DevFixture -Status 'FAIL'
        $result = Invoke-DevScript -Fixture $fixture -Environment (
            New-Environment -Fixture $fixture -PrsJson '[]'
        )
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.status | Should -Be 'PARTIAL'
        $preview.record.automatedStatus | Should -Be 'FAIL'
        $preview.record.overallStatus | Should -Be 'FAIL'
        Assert-NoDevWrites -Fixture $fixture
    }

    It 'does not let an old commit rerun replace the current dev head evidence' {
        $fixture = New-DevFixture
        $result = Invoke-DevScript -Fixture $fixture -Environment (
            New-Environment -Fixture $fixture -DevHeadJson (New-DevHeadJson -Sha ('4' * 40))
        )
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.status | Should -Be 'PARTIAL'
        $preview.reason | Should -Match 'no longer the current dev head'
        Assert-NoDevWrites -Fixture $fixture
    }

    It 'publishes PARTIAL evidence when the final automated record is incomplete' {
        $fixture = New-DevFixture
        $evidence = Read-FixtureJson -Path $fixture.Evidence
        [void]$evidence.PSObject.Properties.Remove('sdkVersion')
        Write-FixtureJson -Path $fixture.Evidence -Value $evidence

        $result = Invoke-DevScript -Fixture $fixture -Environment (New-Environment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.target.type | Should -Be 'issue'
        $preview.record.automatedStatus | Should -Be 'PARTIAL'
        $preview.record.overallStatus | Should -Be 'PARTIAL'
        $preview.commentBody | Should -Match 'Overall: `PARTIAL`'
    }

    It 'publishes FAIL final evidence and never reverts dev' {
        $fixture = New-DevFixture -Status 'FAIL'
        $result = Invoke-DevScript -Fixture $fixture -Environment (New-Environment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $preview.record.automatedStatus | Should -Be 'FAIL'
        $preview.record.overallStatus | Should -Be 'FAIL'
        $preview.commentBody | Should -Match 'Automated: `FAIL`'
        @(Get-Content -LiteralPath (Join-Path $Fixture.Repo 'scripts\verify.ps1') -Raw) |
            Should -Not -Match 'git (reset|revert|checkout)'
    }

    It 'publishes an early FAIL record even when build hashes were never produced' {
        $fixture = New-DevFixture -Status 'FAIL'
        $evidence = Read-FixtureJson -Path $fixture.Evidence
        $evidence.buildDllSha256 = $null
        $evidence.buildProfileDigest = $null
        $evidence.sdkVersion = $null
        Write-FixtureJson -Path $fixture.Evidence -Value $evidence

        $result = Invoke-DevScript -Fixture $fixture -Environment (New-Environment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.Preview
        $published = Read-FixtureJson -Path $fixture.Result

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $preview.target.type | Should -Be 'issue'
        $preview.record.automatedStatus | Should -Be 'FAIL'
        $preview.record.overallStatus | Should -Be 'FAIL'
        $preview.record.buildDllSha256 | Should -BeNullOrEmpty
        $published.status | Should -Be 'PUBLISHED'
    }

    It 'publishes FAIL when the build failed after recording the profile digest' {
        $fixture = New-DevFixture -Status 'FAIL'
        $evidence = Read-FixtureJson -Path $fixture.Evidence
        $evidence.buildDllSha256 = $null
        Write-FixtureJson -Path $fixture.Evidence -Value $evidence

        $result = Invoke-DevScript -Fixture $fixture -Environment (New-Environment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $preview.record.automatedStatus | Should -Be 'FAIL'
        $preview.record.buildProfileDigest | Should -Be ('b' * 64)
        $preview.record.buildDllSha256 | Should -BeNullOrEmpty
    }

    It 'does not publish incomplete FAIL evidence as complete' {
        $fixture = New-DevFixture -Status 'FAIL'
        $evidence = Read-FixtureJson -Path $fixture.Evidence
        [void]$evidence.PSObject.Properties.Remove('tests')
        Write-FixtureJson -Path $fixture.Evidence -Value $evidence

        $result = Invoke-DevScript -Fixture $fixture -Environment (New-Environment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.record.automatedStatus | Should -Be 'PARTIAL'
        $preview.record.overallStatus | Should -Be 'PARTIAL'
        $preview.commentBody | Should -Match 'Automated: `PARTIAL`'
    }

    It 'does not certify a pull request that is not merged' {
        $fixture = New-DevFixture
        $result = Invoke-DevScript -Fixture $fixture -Environment (
            New-Environment -Fixture $fixture -PrJson (New-PrJson -State 'CLOSED')
        )
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.status | Should -Be 'PARTIAL'
        $preview.record.automatedStatus | Should -Be 'PARTIAL'
        Assert-NoDevWrites -Fixture $fixture
    }

    It 'does not certify an association head that differs from the reviewed PR head' {
        $fixture = New-DevFixture
        $result = Invoke-DevScript -Fixture $fixture -Environment (
            New-Environment -Fixture $fixture `
                -PrsJson (New-PullAssociationJson -HeadSha ('3' * 40))
        )
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.status | Should -Be 'PARTIAL'
        $preview.record.prHeadSha | Should -Be ('3' * 40)
        Assert-NoDevWrites -Fixture $fixture
    }

    It 'writes PARTIAL artifacts when association metadata cannot be parsed' {
        $fixture = New-DevFixture
        $result = Invoke-DevScript -Fixture $fixture -Environment (
            New-Environment -Fixture $fixture -PrsJson '{not-json'
        )
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.status | Should -Be 'PARTIAL'
        $preview.record.automatedStatus | Should -Be 'PARTIAL'
        $preview.target.type | Should -Be 'none'
    }

    It 'writes PARTIAL artifacts when an association field is missing' {
        $fixture = New-DevFixture
        $association = [ordered]@{
            state = 'closed'
            merged_at = '2026-11-14T09:55:00Z'
            head = [ordered]@{ sha = $script:reviewedSha }
        } | ConvertTo-Json -Depth 4 -Compress
        $result = Invoke-DevScript -Fixture $fixture -Environment (
            New-Environment -Fixture $fixture -PrsJson "[$association]"
        )
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.status | Should -Be 'PARTIAL'
        $preview.target.type | Should -Be 'none'
        Assert-NoDevWrites -Fixture $fixture
    }

    It 'does not publish to a pull request declared as the evidence issue' {
        $fixture = New-DevFixture
        $result = Invoke-DevScript -Fixture $fixture -Environment (
            New-Environment -Fixture $fixture `
                -IssueJson '{"number":7,"state":"open","pull_request":{}}'
        )
        $preview = Read-FixtureJson -Path $fixture.Preview

        $result.ExitCode | Should -Not -Be 0 -Because $result.Output
        $preview.status | Should -Be 'PARTIAL'
        $preview.target.type | Should -Be 'none'
        Assert-NoDevWrites -Fixture $fixture
    }

    It 'updates an existing final-dev marker instead of creating a duplicate' {
        $fixture = New-DevFixture
        $comments = @([ordered]@{ id = 88; body = (New-EvidenceComment) }) |
            ConvertTo-Json -Depth 6 -Compress
        $result = Invoke-DevScript -Fixture $fixture -Environment (
            New-Environment -Fixture $fixture -CommentsJson $comments
        )
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Be 0 -Because $result.Output
        @($log | Where-Object { $_ -match 'issues/7/comments.*--method POST' }).Count | Should -Be 0
        @($log | Where-Object { $_ -match 'issues/comments/88.*--method PATCH' }).Count | Should -Be 1
    }
}

Describe 'dev verification workflow boundaries' {
    It 'runs only on dev pushes with read-only contents and issue publication permission' {
        $workflow = Get-Content -Raw -LiteralPath (
            Join-Path $script:sourceRepoRoot '.github\workflows\verify-dev.yml'
        )

        $workflow | Should -Match 'push:'
        $workflow | Should -Match 'branches: \[ "dev" \]'
        $workflow | Should -Match 'contents: read'
        $workflow | Should -Match 'issues: write'
        $workflow | Should -Not -Match 'administration:'
        $workflow | Should -Not -Match 'git (reset|revert|checkout)'
        $workflow | Should -Match 'concurrency:'
        $workflow | Should -Match 'cancel-in-progress: false'
        $workflow | Should -Match 'name: dev-verification-\$\{\{ github\.sha \}\}-\$\{\{ github\.run_attempt \}\}'
    }
}
