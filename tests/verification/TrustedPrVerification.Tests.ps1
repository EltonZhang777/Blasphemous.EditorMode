BeforeAll {
    $script:sourceRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $script:gateSource = Join-Path $script:sourceRepoRoot 'scripts\verify.ps1'
    $script:pwshPath = (Get-Process -Id $PID).Path
    $script:sourceSha = '0123456789abcdef0123456789abcdef01234567'
    $script:otherSha = 'fedcba9876543210fedcba9876543210fedcba98'

    function Write-FixtureJson {
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

    function Read-FixtureJson {
        param(
            [Parameter(Mandatory)]
            [string] $Path
        )

        Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    }

    function New-TransportFixture {
        param(
            [string] $Status = 'PASS',
            [string] $GeneratedAt = ([DateTimeOffset]::UtcNow.ToString('O'))
        )

        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $repo = Join-Path $root 'repo'
        $bin = Join-Path $repo 'bin'
        New-Item -ItemType Directory -Force -Path (Join-Path $repo 'scripts'), $bin | Out-Null
        Copy-Item -LiteralPath $script:gateSource -Destination (Join-Path $repo 'scripts\verify.ps1')
        $automatedEvidence = Join-Path $repo 'artifacts\automated-evidence.json'
        Write-FixtureJson -Path $automatedEvidence -Value ([ordered]@{
            schemaVersion = 1
            evidenceKind = 'automated'
            generatedAt = $GeneratedAt
            sourceSha = $script:sourceSha
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
                checkedFiles = 56
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
        })

        @'
param()

$command = $args -join ' '
if ($env:FAKE_GH_LOG) {
    Add-Content -LiteralPath $env:FAKE_GH_LOG -Value $command
}

if ($args.Count -ge 2 -and $args[0] -eq 'pr' -and $args[1] -eq 'view') {
    [Console]::Out.WriteLine($env:FAKE_GH_PR_JSON)
    exit 0
}

if ($args[0] -ne 'api') {
    exit 2
}

$inputPath = $null
for ($index = 0; $index -lt $args.Count; $index++) {
    if ($args[$index] -eq '--input') {
        $inputPath = $args[$index + 1]
    }
}
if ($inputPath -and $env:FAKE_GH_INPUT_LOG) {
    Add-Content -LiteralPath $env:FAKE_GH_INPUT_LOG -Value "--- $command"
    Add-Content -LiteralPath $env:FAKE_GH_INPUT_LOG -Value (Get-Content -Raw -LiteralPath $inputPath)
}

if ($command -match 'actions/runs/\d+/artifacts' -and $command -notmatch '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_GH_ARTIFACTS_JSON)
    exit 0
}
if ($command -match '/pulls(?:/|\s|$)' -and $command -notmatch '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_GH_PRS_JSON)
    exit 0
}
if ($command -match 'actions/runs/\d+' -and $command -notmatch '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_GH_RUN_JSON)
    exit 0
}
if ($command -match 'check-runs' -and $command -notmatch '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_GH_CHECK_RUNS_JSON)
    exit 0
}
if ($command -match 'issues/\d+/comments' -and $command -notmatch '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_GH_COMMENTS_JSON)
    exit 0
}
if ($command -match 'issues/\d+' -and $command -notmatch '--method (POST|PATCH)') {
    if ($env:FAKE_GH_ISSUE_EXIT -and [int]$env:FAKE_GH_ISSUE_EXIT -ne 0) {
        exit [int]$env:FAKE_GH_ISSUE_EXIT
    }
    [Console]::Out.WriteLine($env:FAKE_GH_ISSUE_JSON)
    exit 0
}
if ($command -match 'issues/\d+/comments' -and $command -match '--method POST' -and $env:FAKE_GH_COMMENT_EXIT) {
    exit [int]$env:FAKE_GH_COMMENT_EXIT
}
if ($command -match '--method (POST|PATCH)') {
    [Console]::Out.WriteLine($env:FAKE_GH_WRITE_JSON)
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
            AutomatedEvidence = $automatedEvidence
            GeneratedAt = $GeneratedAt
            Transport = Join-Path $root 'transport.json'
            GhLog = Join-Path $root 'gh.log'
            GhInputLog = Join-Path $root 'gh-input.log'
            PublicationPreview = Join-Path $repo 'artifacts\publication-preview.json'
            PublicationResult = Join-Path $repo 'artifacts\publication-result.json'
            CheckRunRequest = Join-Path $repo 'artifacts\check-run-request.json'
        }
    }

    function Invoke-FixtureScript {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture,
            [string[]] $Arguments = @(),
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
            $output = & $script:pwshPath -NoProfile -NonInteractive -File (Join-Path $Fixture.Repo 'scripts\verify.ps1') @Arguments 2>&1
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

    function New-TransportArguments {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture,
            [string] $Status = 'PASS',
            [string] $WorkflowConclusion = 'success',
            [string] $WorkflowName = 'PR verification',
            [string] $WorkflowEvent = 'pull_request_target',
            [string] $SourceSha = $script:sourceSha
        )

        @(
            '-Mode', 'transport'
            '-PrNumber', '42'
            '-SourceSha', $SourceSha
            '-Repository', 'owner/repo'
            '-WorkflowRunId', '1234'
            '-WorkflowRunAttempt', '1'
            '-WorkflowName', $WorkflowName
            '-WorkflowPath', '.github/workflows/verify-pr.yml'
            '-WorkflowEvent', $WorkflowEvent
            '-WorkflowConclusion', $WorkflowConclusion
            '-TransportOutputPath', $Fixture.Transport
        )
    }

    function New-PrJson {
        param(
            [string] $Body = "Evidence issue: #7",
            [string] $HeadSha = $script:sourceSha,
            [string] $BaseRefName = 'dev',
            [string] $State = 'OPEN'
        )

        [ordered]@{
            body = $Body
            headRefOid = $HeadSha
            baseRefName = $BaseRefName
            state = $State
            isDraft = $false
        } | ConvertTo-Json -Compress
    }

    function New-RunJson {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture,
            [string] $Conclusion = 'success',
            [string] $SourceSha = $script:sourceSha,
            [int] $PrNumber = 42,
            [string] $HeadRepository = 'owner/repo',
            [string] $HeadBranch = 'feature',
            [switch] $EmptyPullRequests,
            [string] $CreatedAt = ([DateTimeOffset]::Parse($Fixture.GeneratedAt).AddMinutes(-10).ToString('O'))
        )

        $run = [ordered]@{
            id = 1234
            run_attempt = 1
            name = 'PR verification'
            path = '.github/workflows/verify-pr.yml'
            event = 'pull_request_target'
            status = 'completed'
            conclusion = $Conclusion
            created_at = $CreatedAt
            repository = [ordered]@{ full_name = 'owner/repo' }
            head_repository = [ordered]@{ full_name = $HeadRepository }
            head_branch = $HeadBranch
            head_sha = $SourceSha
            pull_requests = @()
        }
        if (-not $EmptyPullRequests) {
            $run.pull_requests = @([ordered]@{
                number = $PrNumber
                head = [ordered]@{
                    sha = $SourceSha
                    repo = [ordered]@{ full_name = $HeadRepository }
                }
                base = [ordered]@{ ref = 'dev' }
            })
        }
        $run | ConvertTo-Json -Depth 8 -Compress
    }

    function New-PullJson {
        param(
            [int] $Number = 42,
            [string] $HeadSha = $script:sourceSha,
            [string] $HeadRepository = 'owner/repo',
            [string] $HeadBranch = 'feature',
            [string] $State = 'open'
        )

        @([ordered]@{
            number = $Number
            state = $State
            head = [ordered]@{
                sha = $HeadSha
                ref = $HeadBranch
                repo = [ordered]@{ full_name = $HeadRepository }
            }
            base = [ordered]@{ ref = 'dev' }
        }) | ConvertTo-Json -Depth 8 -Compress
    }

    function New-ArtifactsJson {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture,
            [string] $Name = 'pr-verification-1234-1',
            [long] $Size = 2048,
            [bool] $Expired = $false,
            [string] $CreatedAt = $Fixture.GeneratedAt,
            [string] $ExpiresAt = ([DateTimeOffset]::UtcNow.AddDays(1).ToString('O'))
        )

        [ordered]@{
            total_count = 1
            artifacts = @([ordered]@{
                id = 99
                name = $Name
                size_in_bytes = $Size
                expired = $Expired
                created_at = $CreatedAt
                expires_at = $ExpiresAt
                workflow_run = [ordered]@{ id = 1234 }
            })
        } | ConvertTo-Json -Depth 8 -Compress
    }

    function New-IssueJson {
        param(
            [switch] $IsPullRequest,
            [int] $Number = 7
        )

        $issue = [ordered]@{
            number = $Number
            state = 'open'
            title = 'Evidence issue'
        }
        if ($IsPullRequest) {
            $issue.pull_request = [ordered]@{ url = 'https://api.github.test/pulls/7' }
        }
        $issue | ConvertTo-Json -Depth 4 -Compress
    }

    function New-CommentMarkerJson {
        param(
            [string] $GeneratedAt = $script:generatedAt,
            [string] $RunCreatedAt = ([DateTimeOffset]::Parse($script:generatedAt).ToString('O')),
            [long] $RunId = 1234,
            [int] $RunAttempt = 1
        )

        $record = [ordered]@{
            schemaVersion = 1
            evidenceKind = 'pr-head-automated'
            sourceSha = $script:sourceSha
            generatedAt = $GeneratedAt
            automatedStatus = 'PASS'
            runtimeStatus = 'PARTIAL'
            overallStatus = 'PARTIAL'
            prNumber = 42
            evidenceIssue = 7
            workflowRunId = $RunId
            workflowRunAttempt = $RunAttempt
            workflowRunCreatedAt = $RunCreatedAt
            workflowConclusion = 'success'
        }
        $json = $record | ConvertTo-Json -Depth 6 -Compress
        @(
            '<!-- codex-evidence:pr-head-automated -->'
            '## Verification evidence: pr-head-automated'
            ''
            ('- Source SHA: `{0}`' -f $record.sourceSha)
            ''
            '<!-- codex-evidence-json:start -->'
            $json
            '<!-- codex-evidence-json:end -->'
        ) -join "`n"
    }

    function New-PublishEnvironment {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture,
            [string] $PrJson = (New-PrJson),
            [string] $CommentsJson = '[]',
            [string] $CheckRunsJson = '{"total_count":0,"check_runs":[]}',
            [string] $RunJson = $null,
            [string] $ArtifactsJson = $null,
            [string] $IssueJson = $null,
            [string] $PullsJson = '[]',
            [int] $IssueExit = 0
        )

        if (-not $RunJson) {
            $RunJson = New-RunJson -Fixture $Fixture
        }
        if (-not $ArtifactsJson) {
            $ArtifactsJson = New-ArtifactsJson -Fixture $Fixture
        }
        if (-not $IssueJson) {
            $IssueJson = New-IssueJson
        }

        @{
            FAKE_GH_LOG = $Fixture.GhLog
            FAKE_GH_INPUT_LOG = $Fixture.GhInputLog
            FAKE_GH_PR_JSON = $PrJson
            FAKE_GH_COMMENTS_JSON = $CommentsJson
            FAKE_GH_CHECK_RUNS_JSON = $CheckRunsJson
            FAKE_GH_RUN_JSON = $RunJson
            FAKE_GH_ARTIFACTS_JSON = $ArtifactsJson
            FAKE_GH_PRS_JSON = $PullsJson
            FAKE_GH_ISSUE_JSON = $IssueJson
            FAKE_GH_ISSUE_EXIT = $IssueExit
            FAKE_GH_WRITE_JSON = '{"id":501}'
        }
    }

    function New-PublishArguments {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture
        )

        @(
            '-Mode', 'publish-pr'
            '-TransportPath', $Fixture.Transport
            '-Repository', 'owner/repo'
            '-WorkflowRunId', '1234'
            '-WorkflowRunAttempt', '1'
            '-Publish'
        )
    }
}

Describe 'trusted PR verification transport' {
    It 'writes a non-authoritative transport record for a passed verification job' {
        $fixture = New-TransportFixture
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)
        $transport = Read-FixtureJson -Path $fixture.Transport

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $transport.schemaVersion | Should -Be 1
        $transport.transportVersion | Should -Be 1
        $transport.authoritative | Should -BeFalse
        $transport.repository | Should -Be 'owner/repo'
        $transport.prNumber | Should -Be 42
        $transport.sourceSha | Should -Be $script:sourceSha
        $transport.workflow.name | Should -Be 'PR verification'
        $transport.workflow.path | Should -Be '.github/workflows/verify-pr.yml'
        $transport.workflow.event | Should -Be 'pull_request_target'
        $transport.workflow.runId | Should -Be 1234
        $transport.workflow.runAttempt | Should -Be 1
        $transport.workflow.conclusion | Should -Be 'success'
        $transport.evidence.automatedStatus | Should -Be 'PASS'
        $transport.evidence.sourceSha | Should -Be $script:sourceSha
    }

    It 'rejects a source SHA that differs from the verified evidence' {
        $fixture = New-TransportFixture
        $arguments = New-TransportArguments -Fixture $fixture -SourceSha $script:otherSha
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments $arguments

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'source SHA'
        (Test-Path -LiteralPath $fixture.Transport) | Should -BeFalse
    }

    It 'rejects a conclusion that disagrees with automated evidence' {
        $fixture = New-TransportFixture -Status 'PASS'
        $arguments = New-TransportArguments -Fixture $fixture -Status 'PASS' -WorkflowConclusion 'failure'
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments $arguments

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'conclusion'
        (Test-Path -LiteralPath $fixture.Transport) | Should -BeFalse
    }
}

Describe 'trusted PR evidence publication' {
    It 'publishes one PR marker and then completes the fixed check' {
        $fixture = New-TransportFixture
        $transportResult = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)
        $transportResult.ExitCode | Should -Be 0 -Because $transportResult.Output

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.PublicationPreview
        $published = Read-FixtureJson -Path $fixture.PublicationResult
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $preview.target.type | Should -Be 'pr'
        $preview.target.number | Should -Be 42
        $preview.record.automatedStatus | Should -Be 'PASS'
        $preview.record.runtimeStatus | Should -Be 'PARTIAL'
        $preview.record.prNumber | Should -Be 42
        $preview.record.evidenceIssue | Should -Be 7
        $preview.record.workflowRunId | Should -Be 1234
        $preview.record.workflowRunAttempt | Should -Be 1
        $preview.record.transportArtifactId | Should -Be 99
        $preview.record.transportArtifactName | Should -Be 'pr-verification-1234-1'
        $preview.record.transportArtifactSize | Should -Be 2048
        $published.status | Should -Be 'PUBLISHED'
        $checkRequest.name | Should -Be 'Trusted verification evidence'
        $checkRequest.head_sha | Should -Be $script:sourceSha
        $checkRequest.conclusion | Should -Be 'success'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method POST' }).Count | Should -Be 1
        @($log | Where-Object { $_ -match 'check-runs.*--method POST' }).Count | Should -Be 1
        $commentIndex = [array]::IndexOf($log, @($log | Where-Object { $_ -match 'issues/42/comments.*--method POST' })[0])
        $checkIndex = [array]::IndexOf($log, @($log | Where-Object { $_ -match 'check-runs.*--method POST' })[0])
        $checkIndex | Should -BeGreaterThan $commentIndex
    }

    It 'resolves an empty fork run association from the run head branch' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -RunJson (
                New-RunJson -Fixture $fixture -HeadRepository 'contributor/Blasphemous.EditorMode' -EmptyPullRequests
            ) -PullsJson (
                New-PullJson -HeadRepository 'contributor/Blasphemous.EditorMode'
            ))
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $checkRequest.conclusion | Should -Be 'success'
        $checkRequest.head_sha | Should -Be $script:sourceSha
    }

    It 'rejects a fork run without one matching open PR' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -RunJson (
                New-RunJson -Fixture $fixture -HeadRepository 'contributor/Blasphemous.EditorMode' -EmptyPullRequests
            ))
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'exactly one'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'publishes failed evidence and records a failed check' {
        $fixture = New-TransportFixture -Status 'FAIL'
        $transportResult = Invoke-FixtureScript -Fixture $fixture -Arguments (
            New-TransportArguments -Fixture $fixture -Status 'FAIL' -WorkflowConclusion 'failure'
        )
        $transportResult.ExitCode | Should -Be 0 -Because $transportResult.Output

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -RunJson (
                New-RunJson -Fixture $fixture -Conclusion 'failure'
            ))
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $checkRequest.conclusion | Should -Be 'failure'
        $checkRequest.head_sha | Should -Be $script:sourceSha
    }

    It 'updates an existing check instead of creating another one' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)
        $checkRuns = '{"total_count":1,"check_runs":[{"id":77,"name":"Trusted verification evidence"}]}'

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -CheckRunsJson $checkRuns)
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $checkRequest.conclusion | Should -Be 'success'
        @($log | Where-Object { $_ -match 'check-runs/77.*--method PATCH' }).Count | Should -Be 1
        @($log | Where-Object { $_ -match 'check-runs.*--method POST' }).Count | Should -Be 0
    }

    It 'rejects stale transport without comment or check writes' {
        $oldTime = [DateTimeOffset]::UtcNow.AddHours(-2).ToString('O')
        $fixture = New-TransportFixture -GeneratedAt $oldTime
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture)
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'stale'
        @($log | Where-Object { $_ -match '--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'rejects a stale head without publishing a success' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -PrJson (New-PrJson -HeadSha $script:otherSha))
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'head SHA'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method POST' }).Count | Should -Be 0
    }

    It 'rejects an invalid evidence issue declaration' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -PrJson (New-PrJson -Body 'No evidence issue'))
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'exactly one'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method POST' }).Count | Should -Be 0
    }

    It 'rejects a mismatched workflow identity' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (
            New-TransportArguments -Fixture $fixture -WorkflowName 'Untrusted workflow'
        )

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture)
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'workflow identity'
        $checkRequest.conclusion | Should -Be 'failure'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'records a failed check when marker publication fails' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)
        $environment = New-PublishEnvironment -Fixture $fixture
        $environment.FAKE_GH_COMMENT_EXIT = 1

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment $environment
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $checkRequest.conclusion | Should -Be 'failure'
        @($log | Where-Object { $_ -match 'check-runs.*--method POST' }).Count | Should -Be 1
    }

    It 'validates the run and artifact before publishing' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture)
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Be 0 -Because $result.Output
        @($log | Where-Object { $_ -match 'actions/runs/1234' -and $_ -notmatch 'artifacts' }).Count | Should -Be 1
        @($log | Where-Object { $_ -match 'actions/runs/1234/artifacts' }).Count | Should -Be 1
        @($log | Where-Object { $_ -match 'issues/7' -and $_ -notmatch 'comments' }).Count | Should -Be 1
    }

    It 'rejects an unrelated PR association and records a failed check on the run head' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -RunJson (
                New-RunJson -Fixture $fixture -SourceSha $script:otherSha
            ))
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'head SHA'
        $checkRequest.head_sha | Should -Be $script:otherSha
        $checkRequest.conclusion | Should -Be 'failure'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'rejects an expired transport artifact before comment publication' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -ArtifactsJson (
                New-ArtifactsJson -Fixture $fixture -Expired $true
            ))
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'expired'
        $checkRequest.conclusion | Should -Be 'failure'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'rejects an oversized transport artifact before comment publication' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -ArtifactsJson (
                New-ArtifactsJson -Fixture $fixture -Size 262145
            ))
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'too large'
        $checkRequest.conclusion | Should -Be 'failure'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'rejects an evidence issue that is a pull request' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -IssueJson (New-IssueJson -IsPullRequest))
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'pull request'
        $checkRequest.conclusion | Should -Be 'failure'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'rejects a missing evidence issue before comment publication' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -IssueExit 1)
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'cannot be read'
        $checkRequest.conclusion | Should -Be 'failure'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'rejects zero executed tests in a PASS transport' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)
        $transport = Read-FixtureJson -Path $fixture.Transport
        $transport.evidence.tests.executed = 0
        $transport.evidence.tests.passed = 0
        Write-FixtureJson -Path $fixture.Transport -Value $transport

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture)
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'inconsistent'
        $checkRequest.conclusion | Should -Be 'failure'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'rejects a PASS transport missing a required schema field' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)
        $transport = Read-FixtureJson -Path $fixture.Transport
        [void]$transport.evidence.tests.PSObject.Properties.Remove('project')
        Write-FixtureJson -Path $fixture.Transport -Value $transport

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture)
        $checkRequest = Read-FixtureJson -Path $fixture.CheckRunRequest
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'field is missing'
        $checkRequest.conclusion | Should -Be 'failure'
        @($log | Where-Object { $_ -match 'issues/42/comments.*--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'does not let an older failed run replace a newer evidence check' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)
        $newerRunCreatedAt = [DateTimeOffset]::UtcNow.AddMinutes(5).ToString('O')
        $comments = @([ordered]@{
            id = 89
            body = New-CommentMarkerJson -GeneratedAt ([DateTimeOffset]::UtcNow.AddMinutes(4).ToString('O')) `
                -RunCreatedAt $newerRunCreatedAt -RunId 1235 -RunAttempt 1
        }) | ConvertTo-Json -Depth 6 -Compress

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -CommentsJson $comments -ArtifactsJson (
                New-ArtifactsJson -Fixture $fixture -Expired $true
            ))
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'expired'
        @($log | Where-Object { $_ -match '--method (POST|PATCH)' }).Count | Should -Be 0
        (Test-Path -LiteralPath $fixture.CheckRunRequest) | Should -BeFalse
    }

    It 'does not let an older run overwrite newer evidence or its check' {
        $fixture = New-TransportFixture
        $null = Invoke-FixtureScript -Fixture $fixture -Arguments (New-TransportArguments -Fixture $fixture)
        $newerRunCreatedAt = [DateTimeOffset]::UtcNow.AddMinutes(5).ToString('O')
        $comments = @([ordered]@{
            id = 88
            body = New-CommentMarkerJson -GeneratedAt ([DateTimeOffset]::UtcNow.AddMinutes(4).ToString('O')) `
                -RunCreatedAt $newerRunCreatedAt -RunId 1235 -RunAttempt 1
        }) | ConvertTo-Json -Depth 6 -Compress

        $result = Invoke-FixtureScript -Fixture $fixture `
            -Arguments (New-PublishArguments -Fixture $fixture) `
            -Environment (New-PublishEnvironment -Fixture $fixture -CommentsJson $comments)
        $log = @(Get-Content -LiteralPath $fixture.GhLog)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'stale'
        @($log | Where-Object { $_ -match '--method (POST|PATCH)' }).Count | Should -Be 0
        (Test-Path -LiteralPath $fixture.CheckRunRequest) | Should -BeFalse
    }
}

Describe 'trusted PR workflow boundaries' {
    It 'runs trusted verification for dev pull requests on Windows with read-only permissions' {
        $workflow = Get-Content -Raw -LiteralPath (Join-Path $script:sourceRepoRoot '.github\workflows\verify-pr.yml')

        $workflow | Should -Match 'pull_request_target:'
        $workflow | Should -Match 'branches: \[ "dev" \]'
        $workflow | Should -Match 'runs-on: windows-latest'
        $workflow | Should -Match 'dotnet-version: 8\.0\.x'
        $workflow | Should -Match 'persist-credentials: false'
        $workflow | Should -Match "github\.event\.pull_request\.head\.repo\.full_name"
        $workflow | Should -Match 'retention-days: 1'
        $workflow | Should -Match 'ready_for_review, edited'
        $workflow | Should -Not -Match 'pull_request\.draft'
        $workflow | Should -Not -Match 'secrets\.'
        $workflow | Should -Not -Match 'administration:'
    }

    It 'publishes from an independent default-branch workflow without checking out PR code' {
        $workflow = Get-Content -Raw -LiteralPath (Join-Path $script:sourceRepoRoot '.github\workflows\publish-pr.yml')

        $workflow | Should -Match 'workflow_run:'
        $workflow | Should -Match 'checks: write'
        $workflow | Should -Match 'issues: write'
        $workflow | Should -Match 'pull-requests: write'
        $workflow | Should -Match 'persist-credentials: false'
        $workflow | Should -Not -Match 'pull_request\.head'
        $workflow | Should -Not -Match 'secrets\.'
        $workflow | Should -Not -Match 'administration:'
    }
}
