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

## Commands

Use PowerShell for Git and repository commands in a Windows checkout. The examples here do not require WSL or a Bash shell.
