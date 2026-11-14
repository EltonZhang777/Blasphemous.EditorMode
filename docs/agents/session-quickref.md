# Session quick reference

This tracked guide is valid from any ticket worktree. The ticket worktree named by the handoff is authoritative and is the only worktree this session may modify. The primary checkout is an integration surface, not the ticket worktree.

## Preflight

Run this PowerShell preflight from the ticket worktree before changing files:

```powershell
$top = git rev-parse --show-toplevel
$branch = git branch --show-current
$head = git rev-parse HEAD
$status = @(git status --porcelain)
"TOP=$top"
"BRANCH=$branch"
"HEAD=$head"
"STATUS_COUNT=$($status.Count)"
$status
```

Continue only when `TOP` is the ticket worktree named by the handoff, `BRANCH` and `HEAD` match the handoff, and `STATUS_COUNT` is `0`. Otherwise stop and report the mismatch before doing work.

## Checkout boundaries

Make ticket changes only in the authoritative ticket worktree. In a shared checkout, do not switch branches or overwrite another session's branch or working-tree changes.

## Issue queries and cache

Use a summary read by default:

```powershell
$number = 42
gh issue view $number --json number,title,state,labels,assignees,updatedAt,url
```

The default query omits `body` and comment history; request either explicitly only when the task needs it.

Repeated summary reads may use the Git-ignored `.temp/issues/<number>.json` cache instead of invoking `gh` again. Each entry records `issue` (number), `source` (repository), `retrievedAt` (UTC), `fields` fetched, and the returned `summary`; reuse it only when the issue, source, and fields match.

To refresh, bypass the cache and rerun the default summary query. Replace the existing entry for the same issue so its `retrievedAt` and `summary` are updated; matching repeated reads can then use the refreshed entry.

A cached summary is non-authoritative for writes, assignment, labeling, closing, and verification claims. Read an authoritative summary from GitHub before and after every issue write, and again before a state transition; use the refresh path to replace the cached entry.

## Local overlay

`AGENTS.local.md` is ignored, optional machine-specific context. Read it when present, but do not require it; this guide and all tracked repository instructions must work without it.

## Handoff approvals

Cross-repository handoff and approval inheritance follow `AOC-GLOBAL@1`. Approval records use its `source` value for the source thread and user message, plus `action`, `normalized_target`, `lifetime`, and `invalidators`. The only valid lifetime values are `one-time` (one use) and `task` (until task completion or an invalidator).

In this repository, `normalized_target` names the ticket worktree and issue named by the handoff. Unlisted actions or changed targets require new approval. Elevation grants retain the baseline's `constraints.command`, `constraints.host`, `constraints.target`, and `constraints.reuse` values; all must match exactly.

Technical permissions and denials remain independently gated. A handoff does not add authority for issue creation, issue status changes, issue linking, pull requests, or merges.

## Commands

Use PowerShell for Git and repository commands in a Windows checkout. The examples here do not require WSL or a Bash shell.

## Closure evidence

The field and status contract is defined by [#29](https://github.com/EltonZhang777/Blasphemous.EditorMode/issues/29); the canonical verification entrypoint is defined by [#31](https://github.com/EltonZhang777/Blasphemous.EditorMode/issues/31). This section does not repeat or invent build, test, whitespace, profile-smoke, or workflow commands.

Every close record identifies the evidence kind, the full 40-character source SHA, the final merged SHA where applicable, the automated status, the runtime status, and evidence links.

A completed close requires current automated and runtime PASS evidence at the exact HEAD and no open remaining scope. Automated failures and zero-test runs are FAIL. Missing, stale, mismatched, or incomplete runtime evidence is PARTIAL, never PASS. A later HEAD or changed verification scope invalidates the record and requires re-verification.

An explicit PARTIAL close records the delivered scope, remaining scope, and links to follow-up coverage that already exists. Keep the ticket open when that coverage or issue write permission is unavailable.
