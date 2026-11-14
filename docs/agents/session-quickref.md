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

## Local overlay

`AGENTS.local.md` is ignored, optional machine-specific context. Read it when present, but do not require it; this guide and all tracked repository instructions must work without it.

## Commands

Use PowerShell for Git and repository commands in a Windows checkout. The examples here do not require WSL or a Bash shell.
