$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Assert-Equal {
    param(
        [object]$Expected,
        [object]$Actual,
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "ASSERTION FAILED: $Message`nExpected: $Expected`nActual:   $Actual"
    }
}

function Get-IssueJsonFields {
    param([string]$Text)

    $fields = foreach ($command in [regex]::Matches($Text, 'gh issue (?:view|list)[^\r\n`]*?--json\s+(?<fields>[A-Za-z,]+)')) {
        $command.Groups['fields'].Value.Split(',') | ForEach-Object { $_.Trim() }
    }

    return @($fields)
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$quickrefPath = Join-Path $repoRoot 'docs\agents\session-quickref.md'
$issueTrackerPath = Join-Path $repoRoot 'docs\agents\issue-tracker.md'
$quickref = Get-Content -LiteralPath $quickrefPath -Raw
$issueTracker = Get-Content -LiteralPath $issueTrackerPath -Raw

$cacheHeading = '## Issue queries and cache'
$localOverlayHeading = '## Local overlay'
$cacheIndex = $quickref.IndexOf($cacheHeading, [StringComparison]::Ordinal)
$localOverlayIndex = $quickref.IndexOf($localOverlayHeading, [StringComparison]::Ordinal)

Assert-True ($cacheIndex -ge 0) 'quickref must define the issue query and cache section'
Assert-True ($localOverlayIndex -gt $cacheIndex) 'issue query and cache section must precede Local overlay'
Assert-Equal $localOverlayIndex $quickref.IndexOf('## ', $cacheIndex + $cacheHeading.Length) 'issue query and cache must be immediately before Local overlay'

$cacheSection = $quickref.Substring($cacheIndex, $localOverlayIndex - $cacheIndex)
$defaultQuery = [regex]::Match($cacheSection, 'gh issue view \$number --json (?<fields>[A-Za-z,]+)')
Assert-True $defaultQuery.Success 'quickref must show a default summary query'

$defaultFields = @($defaultQuery.Groups['fields'].Value.Split(',') | ForEach-Object { $_.Trim() })
Assert-Equal 'number,title,state,labels,assignees,updatedAt,url' ($defaultFields -join ',') 'default query fields must be summary-only'
Assert-True (-not ($defaultFields -contains 'body')) 'default query must not fetch issue bodies'
Assert-True (-not ($defaultFields -contains 'comments')) 'default query must not fetch comment history'

foreach ($required in @('.temp/issues/', '`issue`', '`source`', '`retrievedAt`', '`fields`', 'non-authoritative for writes, assignment, labeling, closing, and verification claims', 'after every issue write, and again before a state transition', 'To refresh, bypass the cache and rerun the default summary query.', 'Replace the existing entry for the same issue', '`retrievedAt` and `summary` are updated')) {
    Assert-True ($cacheSection.IndexOf($required, [StringComparison]::Ordinal) -ge 0) "quickref cache section must mention: $required"
}

$issueFields = Get-IssueJsonFields $issueTracker
Assert-True ($issueFields.Count -gt 0) 'issue-tracker must use JSON field selection'
Assert-True (-not ($issueFields -contains 'body')) 'issue-tracker default issue reads must not fetch bodies'
Assert-True (-not ($issueFields -contains 'comments')) 'issue-tracker default issue reads must not fetch comments'

$issueReadCommands = [regex]::Matches("$quickref`n$issueTracker", 'gh issue view [^\r\n`]*')
Assert-True ($issueReadCommands.Count -gt 0) 'documentation must show issue view commands'
foreach ($command in $issueReadCommands) {
    Assert-True ($command.Value -match '--json\s+') 'every issue view example must request explicit JSON fields'
    Assert-True ($command.Value -notmatch '--comments') 'issue view examples must not request full comments by default'
}

Assert-True ($issueTracker.IndexOf('--body-file -', [StringComparison]::Ordinal) -ge 0) 'multi-line issue creation must use PowerShell-compatible stdin'
Assert-True (($issueTracker.IndexOf("@'", [StringComparison]::Ordinal) -ge 0) -and ($issueTracker.IndexOf("'@", [StringComparison]::Ordinal) -ge 0)) 'multi-line issue creation must show a PowerShell here-string'
Assert-True (-not ([regex]::IsMatch($issueTracker, '(?im)(?:^|\s)(?:cat\s+)?<<-?\s*["'']?\w+'))) 'issue-tracker must not require a Bash heredoc'
Assert-True (-not ([regex]::IsMatch($issueTracker, '```(?:bash|sh|shell)\b'))) 'issue-tracker code fences must not require Bash'
Assert-True ($issueTracker.IndexOf('--comments', [StringComparison]::Ordinal) -lt 0) 'issue-tracker must not require a full comment read'
Assert-True ($issueTracker.IndexOf("'@me'", [StringComparison]::Ordinal) -ge 0) 'PowerShell examples must quote @me'
Assert-True ($issueTracker.IndexOf('session-quickref.md#issue-queries-and-cache', [StringComparison]::Ordinal) -ge 0) 'issue-tracker must link to the canonical cache section'
Assert-True ($issueTracker.IndexOf('.temp/issues/', [StringComparison]::Ordinal) -lt 0) 'issue-tracker must not repeat the cache path'
Assert-True ($issueTracker.IndexOf('retrievedAt', [StringComparison]::Ordinal) -lt 0) 'issue-tracker must not repeat the cache schema'
Assert-True ($issueTracker.IndexOf('non-authoritative', [StringComparison]::Ordinal) -lt 0) 'issue-tracker must not repeat the cache policy'
Assert-True (-not ([regex]::IsMatch($issueTracker, 'gh issue view\b'))) 'issue-tracker must link the default read instead of duplicating it'

Push-Location $repoRoot
try {
    git check-ignore -q -- '.temp/issues/42.json'
    Assert-Equal 0 $LASTEXITCODE '.temp issue cache must be Git-ignored'
} finally {
    Pop-Location
}

$remoteReads = 0
$remoteFields = @()
$cacheKey = '42'
$source = 'EltonZhang777/Blasphemous.EditorMode'

function Get-FixtureRemoteSummary {
    param(
        [int]$Number,
        [string]$Source,
        [string[]]$Fields
    )

    if ($Fields -contains 'body' -or $Fields -contains 'comments') {
        throw 'fixture remote summary must not fetch bodies or comments'
    }

    $script:remoteReads++
    $script:remoteFields = @($Fields)

    $summary = [ordered]@{}
    foreach ($field in $Fields) {
        $summary[$field] = switch ($field) {
            'number' { $Number }
            'title' { "Summary-only issue read $script:remoteReads" }
            'state' { 'OPEN' }
            'labels' { @() }
            'assignees' { @() }
            'updatedAt' { '2026-11-14T00:00:00Z' }
            'url' { "https://github.com/$Source/issues/$Number" }
            default { throw "unexpected summary field: $field" }
        }
    }

    return [pscustomobject]$summary
}

function Get-FixtureSummary {
    param(
        [string]$Key,
        [string]$Source,
        [string[]]$Fields,
        [switch]$Refresh
    )

    if (-not $Refresh -and $script:cacheStore.ContainsKey($Key)) {
        $candidate = $script:cacheStore[$Key] | ConvertFrom-Json
        $sameIssue = [string]$candidate.issue -eq $Key
        $sameSource = [string]$candidate.source -eq $Source
        $sameFields = (@($candidate.fields | Sort-Object) -join ',') -eq (@($Fields | Sort-Object) -join ',')
        if ($sameIssue -and $sameSource -and $sameFields) {
            return $candidate.summary
        }
    }

    $summary = Get-FixtureRemoteSummary -Number ([int]$Key) -Source $Source -Fields $Fields
    $entry = [pscustomobject]@{
        issue = [int]$Key
        source = $Source
        retrievedAt = [DateTime]::UtcNow.AddTicks($script:remoteReads).ToString('o')
        fields = @($Fields)
        summary = $summary
    }
    $script:cacheStore[$Key] = $entry | ConvertTo-Json -Depth 5 -Compress

    return $summary
}

$cacheStore = @{}
$alternateSource = 'example/other-repository'
$partialFields = @('number', 'title', 'state')
$first = Get-FixtureSummary -Key $cacheKey -Source $source -Fields $defaultFields
$second = Get-FixtureSummary -Key $cacheKey -Source $source -Fields $defaultFields
$initialEntry = $cacheStore[$cacheKey] | ConvertFrom-Json
$initialRemoteFields = @($remoteFields)

Assert-Equal 1 $remoteReads 'a repeated summary read must use the local cache'
Assert-Equal $cacheKey ([string]$initialEntry.issue) 'cache must record issue identity'
Assert-Equal $source $initialEntry.source 'cache must record the source'
Assert-True (-not [string]::IsNullOrWhiteSpace($initialEntry.retrievedAt)) 'cache must record retrieval time'
Assert-Equal ($defaultFields -join ',') ($initialEntry.fields -join ',') 'cache must record fetched fields'
Assert-Equal ($first | ConvertTo-Json -Compress -Depth 5) ($second | ConvertTo-Json -Compress -Depth 5) 'cached summary must match the original summary'
Assert-Equal ($defaultFields -join ',') ($initialRemoteFields -join ',') 'default read must request only summary fields'

$sourceMismatch = Get-FixtureSummary -Key $cacheKey -Source $alternateSource -Fields $defaultFields
Assert-Equal 2 $remoteReads 'a source mismatch must force a remote summary read'
Assert-True ($sourceMismatch.title -ne $second.title) 'a source mismatch must return a fresh summary'
Assert-Equal $alternateSource (($cacheStore[$cacheKey] | ConvertFrom-Json).source) 'a source mismatch must replace the incompatible entry'

$fieldsMismatch = Get-FixtureSummary -Key $cacheKey -Source $alternateSource -Fields $partialFields
Assert-Equal 3 $remoteReads 'a field mismatch must force a remote summary read'
Assert-True ($fieldsMismatch.title -ne $sourceMismatch.title) 'a field mismatch must return a fresh summary'
$beforeRefresh = $cacheStore[$cacheKey] | ConvertFrom-Json
Assert-Equal ($partialFields -join ',') ($beforeRefresh.fields -join ',') 'a field mismatch must replace the incompatible entry'
Assert-Equal ($partialFields -join ',') ($remoteFields -join ',') 'field-mismatch read must use the newly requested fields'

$null = Get-FixtureSummary -Key $cacheKey -Source $alternateSource -Fields $partialFields
Assert-Equal 3 $remoteReads 'normal repeated reads must use a matching cache entry'

$refreshed = Get-FixtureSummary -Key $cacheKey -Source $alternateSource -Fields $partialFields -Refresh
$afterRefresh = $cacheStore[$cacheKey] | ConvertFrom-Json

Assert-Equal 4 $remoteReads 'explicit refresh must fetch a fresh summary'
Assert-Equal 1 $cacheStore.Count 'explicit refresh must replace rather than duplicate the issue entry'
Assert-Equal $cacheKey ([string]$afterRefresh.issue) 'refresh must retain issue identity'
Assert-Equal $alternateSource $afterRefresh.source 'refresh must retain the source'
Assert-Equal ($partialFields -join ',') ($afterRefresh.fields -join ',') 'refresh must retain the requested fields'
Assert-True ($afterRefresh.retrievedAt -ne $beforeRefresh.retrievedAt) 'refresh must update retrieval time'
Assert-True ($afterRefresh.summary.title -ne $beforeRefresh.summary.title) 'refresh must update the cached summary'
Assert-Equal $refreshed.title $afterRefresh.summary.title 'refresh must return the newly cached summary'

$repeatAfterRefresh = Get-FixtureSummary -Key $cacheKey -Source $alternateSource -Fields $partialFields
Assert-Equal 4 $remoteReads 'normal repeated reads must use the refreshed cache entry'
Assert-Equal $refreshed.title $repeatAfterRefresh.title 'repeated reads must return the refreshed summary'

Write-Output "PASS: remote_reads=$remoteReads cache_entries=$($cacheStore.Count); source/field mismatches refetched; refresh updated retrievedAt and summary; post-refresh repeat reused cache"
