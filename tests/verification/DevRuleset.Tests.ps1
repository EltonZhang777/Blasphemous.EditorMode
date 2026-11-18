BeforeAll {
    $script:repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $script:ruleset = Get-Content -Raw -LiteralPath (
        Join-Path $script:repoRoot '.github\rulesets\dev-required-check.json'
    ) | ConvertFrom-Json
    $script:runbook = Get-Content -Raw -LiteralPath (
        Join-Path $script:repoRoot 'docs\dev-ruleset.md'
    )
}

Describe 'dev required-check ruleset' {
    It 'targets only dev with no bypass actors' {
        $script:ruleset.name | Should -Be 'Dev trusted verification'
        $script:ruleset.target | Should -Be 'branch'
        $script:ruleset.enforcement | Should -Be 'active'
        @($script:ruleset.bypass_actors).Count | Should -Be 0
        @($script:ruleset.conditions.ref_name.include) | Should -Be @('refs/heads/dev')
        @($script:ruleset.conditions.ref_name.exclude).Count | Should -Be 0
    }

    It 'requires the fixed GitHub Actions check and current base' {
        @($script:ruleset.rules).Count | Should -Be 1
        $rule = $script:ruleset.rules[0]
        $rule.type | Should -Be 'required_status_checks'
        $rule.parameters.strict_required_status_checks_policy | Should -BeTrue
        $rule.parameters.do_not_enforce_on_create | Should -BeFalse
        @($rule.parameters.required_status_checks).Count | Should -Be 1
        $rule.parameters.required_status_checks[0].context |
            Should -Be 'Trusted verification evidence'
        $rule.parameters.required_status_checks[0].integration_id | Should -Be 15368
    }

    It 'allows check writes only from the trusted publisher workflow' {
        $verifier = Get-Content -Raw -LiteralPath (
            Join-Path $script:repoRoot '.github\workflows\verify-pr.yml'
        )
        $checkWriters = @(
            Get-ChildItem -LiteralPath (Join-Path $script:repoRoot '.github\workflows') -File |
                Where-Object {
                    (Get-Content -Raw -LiteralPath $_.FullName) -match 'checks:\s*write'
                } |
                Select-Object -ExpandProperty Name
        )

        $verifier | Should -Not -Match 'checks:\s*write'
        ($checkWriters -join ',') | Should -Be 'publish-pr.yml'
    }

    It 'prepares approval, activation, audit, and destructive rollback' {
        $script:runbook | Should -Match 'explicit maintainer approval'
        $script:runbook | Should -Match 'gh api --method POST'
        $script:runbook | Should -Match 'branches/dev/protection'
        $script:runbook |
            Should -Match '--input \.github/rulesets/dev-required-check\.json'
        $script:runbook | Should -Match 'failing-test PR'
        $script:runbook | Should -Match 'zero-executed-test PR'
        $script:runbook | Should -Match 'valid PR'
        $script:runbook | Should -Match 'open, non-draft PRs'
        $script:runbook | Should -Match 'refused merge action'
        $script:runbook | Should -Match 'maintainer approval in #35 before running it'
        $script:runbook | Should -Match 'gh api --method DELETE'
        $script:runbook | Should -Match '<ruleset-id>'
    }
}
