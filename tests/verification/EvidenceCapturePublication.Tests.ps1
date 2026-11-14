BeforeAll {
    $script:sourceRepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $script:gateSource = Join-Path $script:sourceRepoRoot 'scripts\verify.ps1'
    $script:pwshPath = (Get-Process -Id $PID).Path
    $script:sourceSha = '0123456789abcdef0123456789abcdef01234567'
    $script:otherSha = 'fedcba9876543210fedcba9876543210fedcba98'
    $script:generatedAt = '2026-11-14T10:00:00.0000000Z'

    function New-FixtureTextFile {
        param(
            [Parameter(Mandatory)]
            [string] $Path,
            [string] $Content = 'fixture'
        )

        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
        [IO.File]::WriteAllText($Path, $Content + "`n", [Text.UTF8Encoding]::new($false))
    }

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

    function New-EvidenceFixture {
        param(
            [switch] $IncludeOptional,
            [string[]] $MissingRequired = @()
        )

        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $repo = Join-Path $root 'repo'
        $bin = Join-Path $repo 'bin'
        $profile = Join-Path $root 'profile'
        $plugins = Join-Path $profile 'Modding\plugins'
        New-Item -ItemType Directory -Force -Path (Join-Path $repo 'scripts'), $bin, $plugins | Out-Null
        Copy-Item -LiteralPath $script:gateSource -Destination (Join-Path $repo 'scripts\verify.ps1')

        New-FixtureTextFile -Path (Join-Path $plugins 'EditorMode.dll') -Content 'editor-mode'
        foreach ($dependency in @(
            'ModdingAPI.dll'
            'LevelFramework.dll'
            'UIFramework.dll'
        )) {
            if ($dependency -notin $MissingRequired) {
                New-FixtureTextFile -Path (Join-Path $plugins $dependency) -Content $dependency
            }
        }
        if ($IncludeOptional) {
            New-FixtureTextFile -Path (Join-Path $plugins 'LevelFrameworkExtended.dll') -Content 'extended'
        }

        @'
param()

if ($env:FAKE_GH_LOG) {
    Add-Content -LiteralPath $env:FAKE_GH_LOG -Value ($args -join ' ')
}

if ($args.Count -ge 2 -and $args[0] -eq 'pr' -and $args[1] -eq 'view') {
    [Console]::Out.WriteLine($env:FAKE_GH_PR_JSON)
    exit 0
}

if ($args[0] -eq 'api') {
    $command = $args -join ' '
    if ($command -match '--method (POST|PATCH)') {
        [Console]::Out.WriteLine($env:FAKE_GH_WRITE_JSON)
        exit 0
    }

    [Console]::Out.WriteLine($env:FAKE_GH_COMMENTS_JSON)
    exit 0
}

exit 2
'@ | Set-Content -LiteralPath (Join-Path $bin 'fake-gh.ps1') -Encoding utf8NoBOM

        @"
@echo off
"$script:pwshPath" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0fake-gh.ps1" %*
exit /b %errorlevel%
"@ | Set-Content -LiteralPath (Join-Path $bin 'gh.cmd') -Encoding ascii

        [pscustomobject]@{
            Root = $root
            Repo = $repo
            Bin = $bin
            Profile = $profile
            Plugins = $plugins
            GhLog = Join-Path $root 'gh.log'
            SmokeEvidence = Join-Path $repo 'artifacts\smoke-evidence.json'
            AutomatedEvidence = Join-Path $repo 'artifacts\automated-evidence.json'
            PublicationPreview = Join-Path $repo 'artifacts\publication-preview.json'
            PublicationResult = Join-Path $repo 'artifacts\publication-result.json'
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

    function Write-AutomatedEvidence {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture,
            [string] $SourceSha = $script:sourceSha,
            [string] $Status = 'PASS',
            [string] $GeneratedAt = $script:generatedAt
        )

        Write-FixtureJson -Path $Fixture.AutomatedEvidence -Value ([ordered]@{
            schemaVersion = 1
            evidenceKind = 'automated'
            generatedAt = $GeneratedAt
            sourceSha = $SourceSha
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
                checkedFiles = 55
                findings = @()
            }
            automatedStatus = $Status
            sdkVersion = '10.0.401'
            runner = [ordered]@{ os = 'Windows'; machineName = 'fixture' }
            errors = @()
        })
    }

    function Write-SmokeEvidence {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture,
            [string] $SourceSha = $script:sourceSha,
            [string] $Status = 'PASS',
            [string] $GeneratedAt = $script:generatedAt
        )

        Write-FixtureJson -Path $Fixture.SmokeEvidence -Value ([ordered]@{
            schemaVersion = 1
            evidenceKind = 'runtime-smoke'
            generatedAt = $GeneratedAt
            sourceSha = $SourceSha
            deployedDllSha256 = ('c' * 64)
            runtimeProfileHash = ('D' * 64)
            dependencies = @()
            runtimeStatus = $Status
            outcome = $Status
            errors = @()
        })
    }

    function New-PrJson {
        param(
            [string] $Body = "Evidence issue: #7",
            [string] $HeadSha = $script:sourceSha
        )

        [ordered]@{
            body = $Body
            headRefOid = $HeadSha
        } | ConvertTo-Json -Compress
    }

    function New-ExistingMarkerBody {
        param(
            [Parameter(Mandatory)]
            [string] $Kind,
            [Parameter(Mandatory)]
            [string] $GeneratedAt,
            [string] $SourceSha = $script:sourceSha
        )

        $json = [ordered]@{
            schemaVersion = 1
            evidenceKind = $Kind
            sourceSha = $SourceSha
            generatedAt = $GeneratedAt
        } | ConvertTo-Json -Compress
        @(
            "<!-- codex-evidence:$Kind -->"
            "## Verification evidence: $Kind"
            "<!-- codex-evidence-json:start -->"
            $json
            "<!-- codex-evidence-json:end -->"
        ) -join "`n"
    }

    function New-PublishEnvironment {
        param(
            [Parameter(Mandatory)]
            [pscustomobject] $Fixture,
            [string] $PrJson = (New-PrJson),
            [string] $CommentsJson = '[]'
        )

        @{
            FAKE_GH_LOG = $Fixture.GhLog
            FAKE_GH_PR_JSON = $PrJson
            FAKE_GH_COMMENTS_JSON = $CommentsJson
            FAKE_GH_WRITE_JSON = '{"id":501}'
        }
    }
}

Describe 'evidence capture and publication' {
    It 'records an explicitly passed smoke with source, deployed DLL, and optional absence' {
        $fixture = New-EvidenceFixture
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'smoke'
            '-ProfileRoot', $fixture.Profile
            '-SourceSha', $script:sourceSha
            '-GeneratedAt', $script:generatedAt
            '-SmokeOutcome', 'PASS'
        )
        $evidence = Read-FixtureJson -Path $fixture.SmokeEvidence
        $optional = @($evidence.dependencies | Where-Object logicalName -eq 'LevelFrameworkExtended')[0]

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $evidence.sourceSha | Should -Be $script:sourceSha
        ([DateTimeOffset]$evidence.generatedAt).ToUniversalTime().ToString(
            'yyyy-MM-ddTHH:mm:ss.fffffffZ',
            [Globalization.CultureInfo]::InvariantCulture
        ) | Should -Be $script:generatedAt
        $evidence.deployedDllSha256 | Should -Be (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $fixture.Plugins 'EditorMode.dll')).Hash.ToLowerInvariant()
        $evidence.runtimeProfileHash | Should -Match '^[0-9A-F]{64}$'
        $evidence.runtimeStatus | Should -Be 'PASS'
        $evidence.outcome | Should -Be 'PASS'
        @($evidence.dependencies | ForEach-Object logicalName) | Should -Be @(
            'Blasphemous.ModdingAPI'
            'Blasphemous.Framework.Levels'
            'Blasphemous.Framework.UI'
            'LevelFrameworkExtended'
        )
        $optional.status | Should -Be 'absent'
        $optional.sha256 | Should -BeNullOrEmpty
        (Test-Path -LiteralPath $fixture.GhLog) | Should -BeFalse
    }

    It 'records PARTIAL by default instead of claiming a real game smoke' {
        $fixture = New-EvidenceFixture
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'smoke'
            '-ProfileRoot', $fixture.Profile
            '-SourceSha', $script:sourceSha
            '-GeneratedAt', $script:generatedAt
        )
        $evidence = Read-FixtureJson -Path $fixture.SmokeEvidence

        $result.ExitCode | Should -Not -Be 0
        $evidence.runtimeStatus | Should -Be 'PARTIAL'
        $evidence.outcome | Should -Be 'PARTIAL'
    }

    It 'records an explicitly failed smoke result' {
        $fixture = New-EvidenceFixture
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'smoke'
            '-ProfileRoot', $fixture.Profile
            '-SourceSha', $script:sourceSha
            '-GeneratedAt', $script:generatedAt
            '-SmokeOutcome', 'FAIL'
        )
        $evidence = Read-FixtureJson -Path $fixture.SmokeEvidence

        $result.ExitCode | Should -Not -Be 0
        $evidence.runtimeStatus | Should -Be 'FAIL'
        $evidence.outcome | Should -Be 'FAIL'
    }

    It 'changes the profile hash when dependency bytes change' {
        $first = New-EvidenceFixture -IncludeOptional
        $second = New-EvidenceFixture -IncludeOptional
        New-FixtureTextFile -Path (Join-Path $second.Plugins 'ModdingAPI.dll') -Content 'different-bytes'

        $firstResult = Invoke-FixtureScript -Fixture $first -Arguments @(
            '-Mode', 'smoke'
            '-ProfileRoot', $first.Profile
            '-SourceSha', $script:sourceSha
            '-GeneratedAt', $script:generatedAt
            '-SmokeOutcome', 'PASS'
        )
        $secondResult = Invoke-FixtureScript -Fixture $second -Arguments @(
            '-Mode', 'smoke'
            '-ProfileRoot', $second.Profile
            '-SourceSha', $script:sourceSha
            '-GeneratedAt', $script:generatedAt
            '-SmokeOutcome', 'PASS'
        )
        $firstEvidence = Read-FixtureJson -Path $first.SmokeEvidence
        $secondEvidence = Read-FixtureJson -Path $second.SmokeEvidence

        $firstResult.ExitCode | Should -Be 0 -Because $firstResult.Output
        $secondResult.ExitCode | Should -Be 0 -Because $secondResult.Output
        $firstEvidence.runtimeProfileHash | Should -Not -Be $secondEvidence.runtimeProfileHash
    }

    It 'writes FAIL smoke evidence when a required dependency is missing' {
        $fixture = New-EvidenceFixture -MissingRequired @('LevelFramework.dll')
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'smoke'
            '-ProfileRoot', $fixture.Profile
            '-SourceSha', $script:sourceSha
            '-GeneratedAt', $script:generatedAt
        )
        $evidence = Read-FixtureJson -Path $fixture.SmokeEvidence

        $result.ExitCode | Should -Not -Be 0
        $evidence.runtimeStatus | Should -Be 'FAIL'
        $evidence.outcome | Should -Be 'FAIL'
        ($evidence.errors -join "`n") | Should -Match 'LevelFramework.dll'
    }

    It 'publishes the expected target and comment body in dry-run without GitHub writes' {
        $fixture = New-EvidenceFixture
        Write-AutomatedEvidence -Fixture $fixture
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'publish'
            '-EvidenceKind', 'pr-head-automated'
            '-PrNumber', '42'
            '-Repository', 'owner/repo'
        ) -Environment (New-PublishEnvironment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.PublicationPreview
        $log = Get-Content -LiteralPath $fixture.GhLog

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $preview.target.type | Should -Be 'pr'
        $preview.target.number | Should -Be 42
        $preview.action | Should -Be 'create'
        $preview.commentBody | Should -Match 'codex-evidence:pr-head-automated'
        $preview.commentBody | Should -Match 'codex-evidence-json:start'
        $preview.record.automatedStatus | Should -Be 'PASS'
        $preview.record.runtimeStatus | Should -Be 'PARTIAL'
        $preview.record.overallStatus | Should -Be 'PARTIAL'
        @($log | Where-Object { $_ -match '--method (POST|PATCH)' }).Count | Should -Be 0
    }

    It 'derives <Case> statuses from current evidence' -TestCases @(
        @{ Case = 'missing runtime'; Kind = 'dev-automated'; Automated = 'PASS'; Smoke = $null; SmokeSha = '0123456789abcdef0123456789abcdef01234567'; RunAutomated = $true; ExpectedAutomated = 'PASS'; ExpectedRuntime = 'PARTIAL'; ExpectedOverall = 'PARTIAL' }
        @{ Case = 'failed runtime'; Kind = 'runtime-smoke'; Automated = 'PASS'; Smoke = 'FAIL'; SmokeSha = '0123456789abcdef0123456789abcdef01234567'; RunAutomated = $true; ExpectedAutomated = 'PASS'; ExpectedRuntime = 'FAIL'; ExpectedOverall = 'FAIL' }
        @{ Case = 'matching pass'; Kind = 'runtime-smoke'; Automated = 'PASS'; Smoke = 'PASS'; SmokeSha = '0123456789abcdef0123456789abcdef01234567'; RunAutomated = $true; ExpectedAutomated = 'PASS'; ExpectedRuntime = 'PASS'; ExpectedOverall = 'PASS' }
        @{ Case = 'mismatched runtime'; Kind = 'dev-automated'; Automated = 'PASS'; Smoke = 'PASS'; SmokeSha = 'fedcba9876543210fedcba9876543210fedcba98'; RunAutomated = $true; ExpectedAutomated = 'PASS'; ExpectedRuntime = 'PARTIAL'; ExpectedOverall = 'PARTIAL' }
        @{ Case = 'missing automated'; Kind = 'runtime-smoke'; Automated = $null; Smoke = 'PASS'; SmokeSha = '0123456789abcdef0123456789abcdef01234567'; RunAutomated = $false; ExpectedAutomated = 'PARTIAL'; ExpectedRuntime = 'PASS'; ExpectedOverall = 'PARTIAL' }
    ) {
        param(
            $Case,
            $Kind,
            $Automated,
            $Smoke,
            $SmokeSha,
            $RunAutomated,
            $ExpectedAutomated,
            $ExpectedRuntime,
            $ExpectedOverall
        )

        $fixture = New-EvidenceFixture
        if ($RunAutomated) {
            Write-AutomatedEvidence -Fixture $fixture -Status $Automated
        }
        if ($null -ne $Smoke) {
            Write-SmokeEvidence -Fixture $fixture -Status $Smoke -SourceSha $SmokeSha
        }
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'publish'
            '-EvidenceKind', $Kind
            '-PrNumber', '42'
            '-Repository', 'owner/repo'
        ) -Environment (New-PublishEnvironment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.PublicationPreview

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $preview.record.automatedStatus | Should -Be $ExpectedAutomated
        $preview.record.runtimeStatus | Should -Be $ExpectedRuntime
        $preview.record.overallStatus | Should -Be $ExpectedOverall
        $preview.target.type | Should -Be 'issue'
        $preview.target.number | Should -Be 7
    }

    It 'treats runtime evidence older than automated evidence as stale' {
        $fixture = New-EvidenceFixture
        Write-AutomatedEvidence -Fixture $fixture -GeneratedAt '2026-11-14T10:00:00.0000000Z'
        Write-SmokeEvidence -Fixture $fixture -GeneratedAt '2026-11-14T09:00:00.0000000Z'
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'publish'
            '-EvidenceKind', 'dev-automated'
            '-PrNumber', '42'
            '-Repository', 'owner/repo'
        ) -Environment (New-PublishEnvironment -Fixture $fixture)
        $preview = Read-FixtureJson -Path $fixture.PublicationPreview

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $preview.record.automatedStatus | Should -Be 'PASS'
        $preview.record.runtimeStatus | Should -Be 'PARTIAL'
        $preview.record.overallStatus | Should -Be 'PARTIAL'
    }

    It 'rejects <Case> evidence issue declarations' -TestCases @(
        @{ Case = 'missing'; Body = 'No issue declaration'; Error = 'exactly one' }
        @{ Case = 'multiple'; Body = "Evidence issue: #7`nEvidence issue: #8"; Error = 'exactly one' }
        @{ Case = 'malformed'; Body = 'Evidence issue: seven'; Error = 'exactly one' }
        @{ Case = 'roadmap'; Body = 'Evidence issue: #1'; Error = 'Issue #1' }
    ) {
        param($Case, $Body, $Error)

        $fixture = New-EvidenceFixture
        Write-AutomatedEvidence -Fixture $fixture
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'publish'
            '-EvidenceKind', 'dev-automated'
            '-PrNumber', '42'
            '-Repository', 'owner/repo'
        ) -Environment (New-PublishEnvironment -Fixture $fixture -PrJson (New-PrJson -Body $Body))

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match ([regex]::Escape($Error))
        (Test-Path -LiteralPath $fixture.PublicationPreview) | Should -BeFalse
    }

    It 'upserts one existing marker comment with a newer run' {
        $fixture = New-EvidenceFixture
        $olderTime = '2026-11-14T09:00:00.0000000Z'
        $existingBody = New-ExistingMarkerBody -Kind 'runtime-smoke' -GeneratedAt $olderTime
        $comments = ConvertTo-Json -Compress -AsArray @([ordered]@{ id = 77; body = $existingBody })
        Write-AutomatedEvidence -Fixture $fixture
        Write-SmokeEvidence -Fixture $fixture

        $environment = New-PublishEnvironment -Fixture $fixture -CommentsJson $comments
        $environment.FAKE_GH_WRITE_JSON = '{"id":77}'
        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'publish'
            '-EvidenceKind', 'runtime-smoke'
            '-PrNumber', '42'
            '-Repository', 'owner/repo'
            '-Publish'
        ) -Environment $environment
        $published = Read-FixtureJson -Path $fixture.PublicationResult
        $log = Get-Content -LiteralPath $fixture.GhLog

        $result.ExitCode | Should -Be 0 -Because $result.Output
        $published.status | Should -Be 'PUBLISHED'
        $published.action | Should -Be 'update'
        $published.commentId | Should -Be 77
        @($log | Where-Object { $_ -match '--method PATCH' }).Count | Should -Be 1
        @($log | Where-Object { $_ -match 'issues/comments/77' }).Count | Should -Be 1
        @($log | Where-Object { $_ -match 'pulls/' }).Count | Should -Be 0
    }

    It 'rejects a stale run without writing GitHub' {
        $fixture = New-EvidenceFixture
        $newerTime = '2026-11-14T11:00:00.0000000Z'
        $existingBody = New-ExistingMarkerBody -Kind 'runtime-smoke' -GeneratedAt $newerTime -SourceSha $script:otherSha
        $comments = ConvertTo-Json -Compress -AsArray @([ordered]@{ id = 88; body = $existingBody })
        Write-AutomatedEvidence -Fixture $fixture
        Write-SmokeEvidence -Fixture $fixture

        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'publish'
            '-EvidenceKind', 'runtime-smoke'
            '-PrNumber', '42'
            '-Repository', 'owner/repo'
            '-Publish'
        ) -Environment (New-PublishEnvironment -Fixture $fixture -CommentsJson $comments)
        $log = Get-Content -LiteralPath $fixture.GhLog

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'stale'
        @($log | Where-Object { $_ -match '--method (POST|PATCH)' }).Count | Should -Be 0
        (Test-Path -LiteralPath $fixture.PublicationResult) | Should -BeFalse
    }

    It 'rejects multiple marker comments for one evidence kind' {
        $fixture = New-EvidenceFixture
        $existingBody = New-ExistingMarkerBody -Kind 'runtime-smoke' -GeneratedAt '2026-11-14T09:00:00.0000000Z'
        $comments = ConvertTo-Json -Compress -AsArray @(
            [ordered]@{ id = 77; body = $existingBody }
            [ordered]@{ id = 78; body = $existingBody }
        )
        Write-AutomatedEvidence -Fixture $fixture
        Write-SmokeEvidence -Fixture $fixture

        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'publish'
            '-EvidenceKind', 'runtime-smoke'
            '-PrNumber', '42'
            '-Repository', 'owner/repo'
            '-Publish'
        ) -Environment (New-PublishEnvironment -Fixture $fixture -CommentsJson $comments)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'multiple marker comments'
    }

    It 'rejects inconsistent runtime status fields in smoke evidence' {
        $fixture = New-EvidenceFixture
        Write-AutomatedEvidence -Fixture $fixture
        Write-SmokeEvidence -Fixture $fixture
        $smoke = Read-FixtureJson -Path $fixture.SmokeEvidence
        $smoke.runtimeStatus = 'FAIL'
        Write-FixtureJson -Path $fixture.SmokeEvidence -Value $smoke

        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'publish'
            '-EvidenceKind', 'dev-automated'
            '-PrNumber', '42'
            '-Repository', 'owner/repo'
        ) -Environment (New-PublishEnvironment -Fixture $fixture)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'inconsistent runtime status'
        (Test-Path -LiteralPath $fixture.PublicationPreview) | Should -BeFalse
    }

    It 'rejects a secondary evidence file with the wrong kind' {
        $fixture = New-EvidenceFixture
        Write-AutomatedEvidence -Fixture $fixture
        Write-SmokeEvidence -Fixture $fixture
        $smoke = Read-FixtureJson -Path $fixture.SmokeEvidence
        $smoke.evidenceKind = 'automated'
        Write-FixtureJson -Path $fixture.SmokeEvidence -Value $smoke

        $result = Invoke-FixtureScript -Fixture $fixture -Arguments @(
            '-Mode', 'publish'
            '-EvidenceKind', 'dev-automated'
            '-PrNumber', '42'
            '-Repository', 'owner/repo'
        ) -Environment (New-PublishEnvironment -Fixture $fixture)

        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'invalid schema or evidence kind'
        (Test-Path -LiteralPath $fixture.PublicationPreview) | Should -BeFalse
    }
}
