[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredFields = @('source_thread', 'source_message', 'action', 'normalized_target', 'lifetime', 'invalidators')
$action = 'run-command'
$target = 'repo:EltonZhang777/Blasphemous.EditorMode#issue-43'
$consumedOneShots = [System.Collections.Generic.HashSet[string]]::new()

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,
        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Test-NonEmptyText {
    param(
        [object] $Value
    )

    return $Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value)
}

function New-ApprovalRecord {
    return [pscustomobject]@{
        source_thread = 'thread-43-parent'
        source_message = 'user message: run the handoff approvals fixture once'
        action = 'run-command'
        normalized_target = 'repo:EltonZhang777/Blasphemous.EditorMode#issue-43'
        lifetime = 'one-shot'
        invalidators = @('first use', 'action or target change', 'user withdrawal')
    }
}

function Test-ApprovalRecord {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Approval
    )

    foreach ($field in $requiredFields) {
        $property = $Approval.PSObject.Properties[$field]
        if ($null -eq $property) {
            return $false
        }

        if ($field -eq 'invalidators') {
            $invalidators = @($property.Value)
            if ($invalidators.Count -eq 0) {
                return $false
            }

            foreach ($invalidator in $invalidators) {
                if (-not (Test-NonEmptyText $invalidator)) {
                    return $false
                }
            }

            continue
        }

        if (-not (Test-NonEmptyText $property.Value)) {
            return $false
        }
    }

    $constraints = $Approval.PSObject.Properties['constraints']
    if ($null -ne $constraints) {
        if ($null -eq $constraints.Value) {
            return $false
        }

        $constraintsTarget = $constraints.Value.PSObject.Properties['target']
        if ($null -eq $constraintsTarget -or
            -not (Test-NonEmptyText $constraintsTarget.Value) -or
            $constraintsTarget.Value -cne $Approval.normalized_target) {
            return $false
        }
    }

    return $true
}

function Test-ElevationConstraints {
    param(
        [object] $RecordConstraints,
        [object] $RequestedConstraints
    )

    if ($null -eq $RecordConstraints -or $null -eq $RequestedConstraints) {
        return $false
    }

    foreach ($field in @('command', 'host', 'target', 'reuse')) {
        $recordProperty = $RecordConstraints.PSObject.Properties[$field]
        $requestedProperty = $RequestedConstraints.PSObject.Properties[$field]
        if ($null -eq $recordProperty -or $null -eq $requestedProperty) {
            return $false
        }

        if (-not (Test-NonEmptyText $recordProperty.Value) -or
            -not (Test-NonEmptyText $requestedProperty.Value)) {
            return $false
        }

        if ($recordProperty.Value -cne $requestedProperty.Value) {
            return $false
        }
    }

    return $true
}

function Get-ApprovalKey {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Approval
    )

    $parts = @(
        $Approval.source_thread,
        $Approval.source_message,
        $Approval.action,
        $Approval.normalized_target,
        $Approval.lifetime,
        (@($Approval.invalidators) -join [char]30)
    )
    $constraints = $Approval.PSObject.Properties['constraints']
    if ($null -ne $constraints) {
        $parts += @(
            $constraints.Value.command,
            $constraints.Value.host,
            $constraints.Value.target,
            $constraints.Value.reuse
        )
    }

    return $parts -join [char]31
}

function Get-HandoffDecision {
    param(
        [Parameter(Mandatory = $true)]
        [object[]] $Approvals,
        [Parameter(Mandatory = $true)]
        [string] $Action,
        [Parameter(Mandatory = $true)]
        [string] $Target,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]] $ConsumedOneShots,
        [object] $Constraints = $null
    )

    foreach ($approval in $Approvals) {
        if (-not (Test-ApprovalRecord $approval)) {
            continue
        }

        if ($approval.action -cne $Action -or $approval.normalized_target -cne $Target) {
            continue
        }

        $recordConstraints = $approval.PSObject.Properties['constraints']
        if ($null -ne $recordConstraints) {
            if (-not (Test-ElevationConstraints $recordConstraints.Value $Constraints)) {
                continue
            }
        }
        elseif ($null -ne $Constraints) {
            continue
        }

        if ($approval.lifetime -ceq 'one-shot') {
            $approvalKey = Get-ApprovalKey $approval
            if ($ConsumedOneShots.Contains($approvalKey)) {
                continue
            }

            [void] $ConsumedOneShots.Add($approvalKey)
            return 'proceed'
        }

        if ($approval.lifetime -ceq 'reusable') {
            return 'proceed'
        }
    }

    return 'requires-approval'
}

$quickref = Join-Path $PSScriptRoot '..\..\docs\agents\session-quickref.md'
$text = Get-Content -LiteralPath $quickref -Raw
$headingMatches = [regex]::Matches($text, '(?m)^## Handoff approvals\r?$')
Assert-True ($headingMatches.Count -eq 1) 'quickref has exactly one Handoff approvals section'

$handoffIndex = $text.IndexOf('## Handoff approvals', [StringComparison]::Ordinal)
$commandsIndex = $text.IndexOf('## Commands', [StringComparison]::Ordinal)
Assert-True ($handoffIndex -ge 0 -and $commandsIndex -gt $handoffIndex) 'Handoff approvals precedes Commands'

$headingsBeforeCommands = [regex]::Matches($text.Substring(0, $commandsIndex), '(?m)^## (.+?)\r?$')
$previousHeading = $headingsBeforeCommands[$headingsBeforeCommands.Count - 1].Groups[1].Value
Assert-True ($previousHeading -eq 'Handoff approvals') 'Handoff approvals is immediately before Commands'

$section = $text.Substring($handoffIndex, $commandsIndex - $handoffIndex)
foreach ($layer in @('User-intent approval', 'Repository workflow authorization', 'Harness technical permission')) {
    Assert-True ($section.Contains("- **$layer**:")) "section defines $layer"
}

foreach ($field in @('source_thread', 'source_message', 'action', 'normalized_target', 'lifetime', 'invalidators')) {
    Assert-True ($section.Contains("``$field``")) "approval record includes $field"
}

foreach ($field in @('constraints', 'constraints.command', 'constraints.host', 'constraints.target', 'constraints.reuse')) {
    Assert-True ($section.Contains("``$field``")) "elevation record includes $field"
}

Assert-True ($section -match '(?i)Every required field and each invalidator entry must be non-empty') 'required fields and invalidators must be non-empty'
Assert-True ($section.Contains('`one-shot`') -and $section.Contains('`reusable`')) 'approval lifetime supports one-shot and reusable states'
Assert-True ($section.Contains('no other value is valid')) 'unknown approval lifetimes are invalid'
Assert-True ($section -match '(?im)^A handoff inherits only approvals explicitly listed in the handoff\.') 'only listed approvals are inherited'
Assert-True ($section -match '(?im)^Do not infer an unlisted action: request separate approval\.') 'unlisted actions require separate approval'
Assert-True ($section.Contains('An explicitly recorded parent elevation grant is inherited only when its command, host, target, and reuse constraints are stored as non-empty')) 'parent elevation grants require stored constraints'
Assert-True ($section.Contains('must all match the request')) 'parent elevation constraints must match exactly'
Assert-True ($section -match '(?i)Unrecorded sandbox, credential, and network permissions remain independently gated') 'unrecorded technical permissions stay gated'
Assert-True ($section -match '(?im)^A technical denial is a concrete blocker\.') 'technical denials block progress'
Assert-True ($section -match '(?i)Do not retry, elevate, or infer consent automatically') 'technical denials do not trigger automatic retry, elevation, or consent'
Assert-True ($section -match '(?im)^A handoff does not authorize issue creation, issue status changes, issue linking, pull-request creation, or merge\.') 'handoff excludes issue, PR, and merge authority'

$approvals = @(New-ApprovalRecord)
Assert-True (-not ($approvals[0].PSObject.Properties.Name -contains 'used')) 'one-shot consumption state is external to the approval record'

$unlisted = Get-HandoffDecision -Approvals $approvals -Action 'create-pull-request' -Target $target -ConsumedOneShots $consumedOneShots
Assert-True ($unlisted -eq 'requires-approval') 'an unlisted action is blocked'

$changedTarget = Get-HandoffDecision -Approvals $approvals -Action $action -Target "$target-other" -ConsumedOneShots $consumedOneShots
Assert-True ($changedTarget -eq 'requires-approval') 'a changed target is blocked'

$recorded = Get-HandoffDecision -Approvals $approvals -Action $action -Target $target -ConsumedOneShots $consumedOneShots
Assert-True ($recorded -eq 'proceed') 'an exact recorded one-shot approval proceeds without re-asking'

$reused = Get-HandoffDecision -Approvals $approvals -Action $action -Target $target -ConsumedOneShots $consumedOneShots
Assert-True ($reused -eq 'requires-approval') 'the one-shot approval is consumed after one use'

$equivalentApproval = @(New-ApprovalRecord)
$equivalentDecision = Get-HandoffDecision -Approvals $equivalentApproval -Action $action -Target $target -ConsumedOneShots $consumedOneShots
Assert-True ($equivalentDecision -eq 'requires-approval') 'a reconstructed equivalent one-shot record is also blocked after consumption'

foreach ($field in $requiredFields) {
    $candidate = New-ApprovalRecord
    [void] $candidate.PSObject.Properties.Remove($field)
    $decision = Get-HandoffDecision -Approvals @($candidate) -Action $action -Target $target -ConsumedOneShots $consumedOneShots
    Assert-True ($decision -eq 'requires-approval') "a record missing $field is blocked"
}

$emptyInvalidator = New-ApprovalRecord
$emptyInvalidator.invalidators = @('')
$emptyInvalidatorDecision = Get-HandoffDecision -Approvals @($emptyInvalidator) -Action $action -Target $target -ConsumedOneShots $consumedOneShots
Assert-True ($emptyInvalidatorDecision -eq 'requires-approval') 'an empty invalidator is blocked'

$unknownLifetime = New-ApprovalRecord
$unknownLifetime.lifetime = 'session'
$unknownLifetimeDecision = Get-HandoffDecision -Approvals @($unknownLifetime) -Action $action -Target $target -ConsumedOneShots $consumedOneShots
Assert-True ($unknownLifetimeDecision -eq 'requires-approval') 'an unknown lifetime is blocked'

$requestedConstraints = [pscustomobject]@{
    command = 'dotnet test Blasphemous.EditorMode.sln'
    host = 'builder-01'
    target = $target
    reuse = 'one-shot'
}

$mismatchedInternalTargetConstraints = $requestedConstraints | Select-Object *
$mismatchedInternalTargetConstraints.target = "$target-other"
$mismatchedInternalTarget = New-ApprovalRecord
$mismatchedInternalTarget.action = 'run-elevated-command'
$mismatchedInternalTarget | Add-Member -NotePropertyName constraints -NotePropertyValue $mismatchedInternalTargetConstraints
$mismatchedInternalTargetDecision = Get-HandoffDecision -Approvals @($mismatchedInternalTarget) -Action $mismatchedInternalTarget.action -Target $target -ConsumedOneShots $consumedOneShots -Constraints $mismatchedInternalTargetConstraints
Assert-True ($mismatchedInternalTargetDecision -eq 'requires-approval') 'an elevation record whose internal target differs from normalized_target is blocked'

$elevation = New-ApprovalRecord
$elevation.action = 'run-elevated-command'
$elevation | Add-Member -NotePropertyName constraints -NotePropertyValue $requestedConstraints
$elevationDecision = Get-HandoffDecision -Approvals @($elevation) -Action $elevation.action -Target $target -ConsumedOneShots $consumedOneShots -Constraints $requestedConstraints
Assert-True ($elevationDecision -eq 'proceed') 'an exact recorded elevation approval proceeds'

$missingConstraints = New-ApprovalRecord
$missingConstraints.action = 'run-elevated-command'
$missingConstraintsDecision = Get-HandoffDecision -Approvals @($missingConstraints) -Action $missingConstraints.action -Target $target -ConsumedOneShots $consumedOneShots -Constraints $requestedConstraints
Assert-True ($missingConstraintsDecision -eq 'requires-approval') 'an elevation record missing constraints is blocked'

$missingRequestConstraints = New-ApprovalRecord
$missingRequestConstraints.action = 'run-elevated-command'
$missingRequestConstraints | Add-Member -NotePropertyName constraints -NotePropertyValue $requestedConstraints
$missingRequestConstraintsDecision = Get-HandoffDecision -Approvals @($missingRequestConstraints) -Action $missingRequestConstraints.action -Target $target -ConsumedOneShots $consumedOneShots
Assert-True ($missingRequestConstraintsDecision -eq 'requires-approval') 'an elevation request missing constraints is blocked'

foreach ($field in @('command', 'host', 'target', 'reuse')) {
    $candidate = New-ApprovalRecord
    $candidate.action = 'run-elevated-command'
    $candidate | Add-Member -NotePropertyName constraints -NotePropertyValue $requestedConstraints
    $changedConstraints = $requestedConstraints | Select-Object *
    $changedConstraints.PSObject.Properties[$field].Value = "changed-$field"
    $decision = Get-HandoffDecision -Approvals @($candidate) -Action $candidate.action -Target $target -ConsumedOneShots $consumedOneShots -Constraints $changedConstraints
    Assert-True ($decision -eq 'requires-approval') "a changed elevation $field is blocked"
}

'PASS: handoff approval inheritance'
