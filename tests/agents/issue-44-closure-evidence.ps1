#Requires -Version 7

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

function Assert-Equal {
    param(
        [object]$Expected,
        [object]$Actual,
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "Assertion failed: $Message (expected '$Expected', got '$Actual')"
    }
}

function Test-NonEmptyText {
    param([object]$Value)

    return $Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value)
}

function Test-NonEmptyTextCollection {
    param(
        [object]$Values,
        [switch]$AllowEmpty
    )

    $items = @($Values)
    if ($items.Count -eq 0) {
        return [bool]$AllowEmpty
    }

    foreach ($item in $items) {
        if (-not (Test-NonEmptyText ([string]$item))) {
            return $false
        }
    }

    return $true
}

function Get-ClosureDisposition {
    param(
        [hashtable]$Record,
        [string]$CurrentHead,
        [bool]$FollowUpsExist,
        [bool]$CanWriteIssue,
        [bool]$VerificationScopeChanged = $false
    )

    foreach ($field in 'evidenceKind', 'sourceSha', 'automatedStatus', 'evidenceLinks') {
        Assert-True ($Record.ContainsKey($field) -and $null -ne $Record[$field]) "closure record has $field"
    }
    Assert-True ($Record.sourceSha -match '^[0-9a-f]{40}$') 'source SHA is a full 40-character SHA'

    $evidenceKind = [string]$Record.evidenceKind
    $sourceSha = [string]$Record.sourceSha
    $automatedStatus = [string]$Record.automatedStatus
    $runtimeStatus = if ($Record.ContainsKey('runtimeStatus')) { [string]$Record.runtimeStatus } else { '' }
    if ([string]::IsNullOrWhiteSpace($runtimeStatus)) {
        $runtimeStatus = 'MISSING'
    }

    $deliveredScope = @()
    $remainingScope = @()
    $followUpLinks = @()
    if ($Record.ContainsKey('deliveredScope')) {
        $deliveredScope = @($Record.deliveredScope)
    }
    if ($Record.ContainsKey('remainingScope')) {
        $remainingScope = @($Record.remainingScope)
    }
    if ($Record.ContainsKey('followUpLinks')) {
        $followUpLinks = @($Record.followUpLinks)
    }

    $finalMergedSha = if ($Record.ContainsKey('finalMergedSha')) { [string]$Record.finalMergedSha } else { '' }
    $mergeEvidence = $evidenceKind -ceq 'final-dev-automated'
    $finalMergedShaValid = -not $mergeEvidence -or $finalMergedSha -match '^[0-9a-f]{40}$'

    $supportedEvidenceKinds = @('pr-head-automated', 'final-dev-automated', 'runtime-smoke', 'documentation-only-automated')
    $documentationOnly = $evidenceKind -ceq 'documentation-only-automated'
    $recordValid = (
        ($supportedEvidenceKinds -ccontains $evidenceKind) -and
        (Test-NonEmptyTextCollection $Record.evidenceLinks) -and
        (Test-NonEmptyTextCollection $deliveredScope) -and
        (Test-NonEmptyTextCollection $remainingScope -AllowEmpty) -and
        (Test-NonEmptyTextCollection $followUpLinks -AllowEmpty) -and
        ($runtimeStatus -cin @('PASS', 'FAIL', 'N/A', 'MISSING', 'PARTIAL'))
    )
    $runtimeCompatible = if ($documentationOnly) {
        $runtimeStatus -ceq 'N/A'
    }
    else {
        $runtimeStatus -ceq 'PASS'
    }
    $zeroTests = $Record.ContainsKey('zeroTests') -and [bool]$Record.zeroTests

    $overallStatus = if ($zeroTests -or $automatedStatus -eq 'FAIL' -or $runtimeStatus -eq 'FAIL') {
        'FAIL'
    }
    elseif (
        -not $recordValid -or
        $VerificationScopeChanged -or
        $sourceSha -cne $CurrentHead -or
        $automatedStatus -ne 'PASS' -or
        -not $runtimeCompatible -or
        -not $finalMergedShaValid
    ) {
        'PARTIAL'
    }
    else {
        'PASS'
    }

    $closure = if (-not $CanWriteIssue -or $VerificationScopeChanged -or $sourceSha -cne $CurrentHead) {
        'OPEN'
    }
    elseif ($overallStatus -eq 'PASS' -and $remainingScope.Count -eq 0) {
        'COMPLETE'
    }
    elseif (
        $overallStatus -ne 'FAIL' -and
        $recordValid -and
        $deliveredScope.Count -gt 0 -and
        $remainingScope.Count -gt 0 -and
        $followUpLinks.Count -gt 0 -and
        $FollowUpsExist
    ) {
        'PARTIAL'
    }
    else {
        'OPEN'
    }

    [pscustomobject]@{ OverallStatus = $overallStatus; Closure = $closure }
}

$quickrefPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\docs\agents\session-quickref.md'))
$quickref = Get-Content -LiteralPath $quickrefPath -Raw
$commandsIndex = $quickref.IndexOf('## Commands', [StringComparison]::Ordinal)
$closureIndex = $quickref.IndexOf('## Closure evidence', [StringComparison]::Ordinal)

Assert-True ($commandsIndex -ge 0) 'quickref has the existing Commands section'
Assert-True ($closureIndex -gt $commandsIndex) 'Closure evidence follows Commands'
Assert-Equal 1 ([regex]::Matches($quickref, '(?m)^## Closure evidence\s*$').Count) 'quickref has one Closure evidence section'

$closureText = $quickref.Substring($closureIndex)
$requiredText = @(
    @{ Pattern = '(?i)full 40-character source SHA'; Message = 'full source SHA is required' },
    @{ Pattern = '(?i)evidence kind'; Message = 'evidence kind is required' },
    @{ Pattern = '(?i)final merged SHA'; Message = 'final merged SHA is recorded when applicable' },
    @{ Pattern = '(?i)automated status'; Message = 'automated status is required' },
    @{ Pattern = '(?i)runtime status'; Message = 'runtime status is required' },
    @{ Pattern = '(?i)evidence links'; Message = 'evidence links are required' },
    @{ Pattern = 'issues/29'; Message = 'quickref cites the field and status contract' },
    @{ Pattern = 'issues/31'; Message = 'quickref cites the canonical entrypoint ticket' },
    @{ Pattern = '(?i)exact HEAD'; Message = 'current exact-HEAD evidence is required' },
    @{ Pattern = '(?i)no open remaining scope'; Message = 'completed closure requires no open remaining scope' },
    @{ Pattern = '(?i)delivered scope'; Message = 'PARTIAL closure records delivered scope' },
    @{ Pattern = '(?i)remaining scope'; Message = 'PARTIAL closure records remaining scope' },
    @{ Pattern = '(?i)follow-up coverage'; Message = 'PARTIAL closure links existing follow-up coverage' },
    @{ Pattern = '(?i)issue write permission'; Message = 'unavailable issue write access keeps the ticket open' },
    @{ Pattern = 'documentation-only-automated'; Message = 'documentation-only evidence kind is explicit' },
    @{ Pattern = 'runtime `N/A`'; Message = 'documentation-only runtime N/A is explicit' },
    @{ Pattern = '(?i)non-empty evidence link'; Message = 'evidence links cannot be blank' },
    @{ Pattern = '(?i)stale'; Message = 'stale evidence is handled' },
    @{ Pattern = '(?i)mismatched'; Message = 'mismatched evidence is handled' },
    @{ Pattern = '(?i)incomplete'; Message = 'incomplete evidence is handled' },
    @{ Pattern = '(?i)unsupported'; Message = 'unsupported evidence is rejected' },
    @{ Pattern = '(?i)zero-test'; Message = 'zero-test runs are handled' },
    @{ Pattern = '(?i)re-verification'; Message = 'HEAD and scope changes require re-verification' }
)
foreach ($assertion in $requiredText) {
    Assert-True ($closureText -match $assertion.Pattern) $assertion.Message
}
Assert-True ($closureText -notmatch '```') 'closure guidance does not duplicate command blocks'

$exactHead = '0123456789abcdef0123456789abcdef01234567'
$completedRecord = @{
    evidenceKind = 'final-dev-automated'
    sourceSha = $exactHead
    finalMergedSha = $exactHead
    automatedStatus = 'PASS'
    runtimeStatus = 'PASS'
    evidenceLinks = @('https://github.com/EltonZhang777/Blasphemous.EditorMode/issues/44')
    zeroTests = $false
    deliveredScope = @('closure contract')
    remainingScope = @()
    followUpLinks = @()
}
Assert-True ($completedRecord.finalMergedSha -match '^[0-9a-f]{40}$') 'final merged SHA is recorded when applicable'

$completed = Get-ClosureDisposition -Record $completedRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PASS' $completed.OverallStatus 'completed evidence is PASS'
Assert-Equal 'COMPLETE' $completed.Closure 'completed closure has current exact-HEAD evidence and no remaining scope'

$blockedRecord = $completedRecord.Clone()
$blockedRecord.runtimeStatus = 'MISSING'
$blockedRecord.remainingScope = @('runtime verification')
$blocked = Get-ClosureDisposition -Record $blockedRecord -CurrentHead $exactHead -FollowUpsExist $false -CanWriteIssue $true
Assert-Equal 'PARTIAL' $blocked.OverallStatus 'missing runtime evidence is PARTIAL'
Assert-Equal 'OPEN' $blocked.Closure 'missing follow-up coverage blocks an incomplete closure'

$partialRecord = $blockedRecord.Clone()
$partialRecord.runtimeStatus = 'PARTIAL'
$partialRecord.followUpLinks = @('https://github.com/EltonZhang777/Blasphemous.EditorMode/issues/45')
$partial = Get-ClosureDisposition -Record $partialRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $partial.OverallStatus 'incomplete runtime evidence is PARTIAL'
Assert-Equal 'PARTIAL' $partial.Closure 'valid explicit PARTIAL closure is allowed with existing follow-up coverage'

$noWrite = Get-ClosureDisposition -Record $partialRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $false
Assert-Equal 'OPEN' $noWrite.Closure 'missing issue write permission keeps the ticket open'

$verifiedDeliveredScope = $partialRecord.Clone()
$verifiedDeliveredScope.runtimeStatus = 'PASS'
$partialWithRemainingScope = Get-ClosureDisposition -Record $verifiedDeliveredScope -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $partialWithRemainingScope.Closure 'remaining scope with existing follow-up coverage uses an explicit PARTIAL close'

$zeroTestRecord = $completedRecord.Clone()
$zeroTestRecord.zeroTests = $true
$zeroTests = Get-ClosureDisposition -Record $zeroTestRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'FAIL' $zeroTests.OverallStatus 'zero-test runs are FAIL'
Assert-Equal 'OPEN' $zeroTests.Closure 'zero-test runs cannot close the ticket'

$failedRecord = $completedRecord.Clone()
$failedRecord.automatedStatus = 'FAIL'
$failed = Get-ClosureDisposition -Record $failedRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'FAIL' $failed.OverallStatus 'automated failures are FAIL'
Assert-Equal 'OPEN' $failed.Closure 'automated failures cannot close the ticket'

$laterHead = Get-ClosureDisposition -Record $completedRecord -CurrentHead '76543210fedcba9876543210fedcba9876543210' -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'OPEN' $laterHead.Closure 'a later HEAD requires re-verification'

$missingAutomatedRecord = $completedRecord.Clone()
$missingAutomatedRecord.automatedStatus = 'MISSING'
$missingAutomated = Get-ClosureDisposition -Record $missingAutomatedRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $missingAutomated.OverallStatus 'missing automated evidence is PARTIAL'
Assert-Equal 'OPEN' $missingAutomated.Closure 'missing automated evidence cannot complete'

$missingRuntimeRecord = $completedRecord.Clone()
$missingRuntimeRecord.Remove('runtimeStatus')
$missingRuntime = Get-ClosureDisposition -Record $missingRuntimeRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $missingRuntime.OverallStatus 'missing runtime evidence is PARTIAL'
Assert-Equal 'OPEN' $missingRuntime.Closure 'missing runtime evidence keeps the ticket open'

$nullRuntimeRecord = $completedRecord.Clone()
$nullRuntimeRecord.runtimeStatus = $null
$nullRuntime = Get-ClosureDisposition -Record $nullRuntimeRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $nullRuntime.OverallStatus 'null runtime evidence is PARTIAL'
Assert-Equal 'OPEN' $nullRuntime.Closure 'null runtime evidence keeps the ticket open'

$changedScope = Get-ClosureDisposition -Record $completedRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true -VerificationScopeChanged $true
Assert-Equal 'PARTIAL' $changedScope.OverallStatus 'changed verification scope is PARTIAL'
Assert-Equal 'OPEN' $changedScope.Closure 'changed verification scope requires re-verification'

$missingMergeShaRecord = $completedRecord.Clone()
$missingMergeShaRecord.Remove('finalMergedSha')
$missingMergeSha = Get-ClosureDisposition -Record $missingMergeShaRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $missingMergeSha.OverallStatus 'missing final merged SHA is PARTIAL for merge evidence'
Assert-Equal 'OPEN' $missingMergeSha.Closure 'missing final merged SHA cannot complete merge evidence'

$invalidMergeShaRecord = $completedRecord.Clone()
$invalidMergeShaRecord.finalMergedSha = 'not-a-sha'
$invalidMergeSha = Get-ClosureDisposition -Record $invalidMergeShaRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $invalidMergeSha.OverallStatus 'invalid final merged SHA is PARTIAL for merge evidence'
Assert-Equal 'OPEN' $invalidMergeSha.Closure 'invalid final merged SHA cannot complete merge evidence'

$nonMergeRecord = $completedRecord.Clone()
$nonMergeRecord.evidenceKind = 'pr-head-automated'
$nonMergeRecord.Remove('finalMergedSha')
$nonMerge = Get-ClosureDisposition -Record $nonMergeRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'COMPLETE' $nonMerge.Closure 'non-merge evidence does not require a final merged SHA'

$documentationRecord = $completedRecord.Clone()
$documentationRecord.evidenceKind = 'documentation-only-automated'
$documentationRecord.Remove('finalMergedSha')
$documentationRecord.runtimeStatus = 'N/A'
$documentation = Get-ClosureDisposition -Record $documentationRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PASS' $documentation.OverallStatus 'documentation-only runtime N/A is valid evidence'
Assert-Equal 'COMPLETE' $documentation.Closure 'documentation-only evidence can complete without a runtime surface'

$missingDocumentationRuntime = $documentationRecord.Clone()
$missingDocumentationRuntime.Remove('runtimeStatus')
$missingDocumentation = Get-ClosureDisposition -Record $missingDocumentationRuntime -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $missingDocumentation.OverallStatus 'missing documentation runtime status is not inferred as N/A'
Assert-Equal 'OPEN' $missingDocumentation.Closure 'missing documentation runtime status cannot complete'

$runtimeNotApplicableRecord = $completedRecord.Clone()
$runtimeNotApplicableRecord.runtimeStatus = 'N/A'
$runtimeNotApplicable = Get-ClosureDisposition -Record $runtimeNotApplicableRecord -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $runtimeNotApplicable.OverallStatus 'runtime N/A is invalid for runtime-bearing evidence'
Assert-Equal 'OPEN' $runtimeNotApplicable.Closure 'runtime N/A cannot complete runtime-bearing evidence'

$unsupportedEvidenceKind = $completedRecord.Clone()
$unsupportedEvidenceKind.evidenceKind = 'manual-assertion'
$unsupported = Get-ClosureDisposition -Record $unsupportedEvidenceKind -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $unsupported.OverallStatus 'unsupported evidence kinds are rejected'
Assert-Equal 'OPEN' $unsupported.Closure 'unsupported evidence kinds cannot close a ticket'

$unsupportedRuntimeStatus = $completedRecord.Clone()
$unsupportedRuntimeStatus.runtimeStatus = 'GREEN'
$unsupportedRuntime = Get-ClosureDisposition -Record $unsupportedRuntimeStatus -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $unsupportedRuntime.OverallStatus 'unsupported runtime statuses are rejected'
Assert-Equal 'OPEN' $unsupportedRuntime.Closure 'unsupported runtime statuses cannot close a ticket'

$blankFieldCases = @(
    @{ Name = 'blank evidence kind'; Field = 'evidenceKind'; Value = ' ' },
    @{ Name = 'blank evidence link'; Field = 'evidenceLinks'; Value = @(' ') },
    @{ Name = 'blank delivered scope'; Field = 'deliveredScope'; Value = @(' ') },
    @{ Name = 'blank remaining scope'; Field = 'remainingScope'; Value = @(' ') },
    @{ Name = 'blank follow-up link'; Field = 'followUpLinks'; Value = @(' ') }
)
foreach ($case in $blankFieldCases) {
    $candidate = $completedRecord.Clone()
    $candidate[$case.Field] = $case.Value
    $decision = Get-ClosureDisposition -Record $candidate -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
    Assert-Equal 'PARTIAL' $decision.OverallStatus "a $($case.Name) is rejected"
    Assert-Equal 'OPEN' $decision.Closure "a $($case.Name) cannot close a ticket"
}

$emptyEvidenceLinks = $completedRecord.Clone()
$emptyEvidenceLinks.evidenceLinks = @()
$emptyEvidence = Get-ClosureDisposition -Record $emptyEvidenceLinks -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $emptyEvidence.OverallStatus 'empty evidence links are rejected'
Assert-Equal 'OPEN' $emptyEvidence.Closure 'empty evidence links cannot close a ticket'

$emptyDeliveredScope = $completedRecord.Clone()
$emptyDeliveredScope.deliveredScope = @()
$emptyScope = Get-ClosureDisposition -Record $emptyDeliveredScope -CurrentHead $exactHead -FollowUpsExist $true -CanWriteIssue $true
Assert-Equal 'PARTIAL' $emptyScope.OverallStatus 'empty delivered scope is rejected'
Assert-Equal 'OPEN' $emptyScope.Closure 'empty delivered scope cannot close a ticket'

Write-Output 'issue-44 closure evidence fixture: PASS'
