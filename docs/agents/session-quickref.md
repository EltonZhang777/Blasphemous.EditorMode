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

Keep these layers distinct:

- **User-intent approval**: an explicit acceptance by the source user of an action on a normalized target. Task scope, a parent-thread summary, or a successful earlier command does not imply it.
- **Repository workflow authorization**: permission from tracked repository instructions and the ticket to perform in-scope local work. It cannot replace user-intent approval or a harness technical permission.
- **Harness technical permission**: the runtime's sandbox, credential, network, and execution decision. It is evaluated at execution time and does not itself constitute user consent.

A handoff inherits only approvals explicitly recorded in the handoff. Each record must contain these fields, and every required field and invalidator entry must be non-empty:

- `source`: the source thread and the exact user message or other verifiable user-instruction evidence.
- `action`: the approved action.
- `normalized_target`: the canonical repository/worktree, issue, or external-resource target. In this repository, a ticket handoff names the ticket worktree and issue.
- `lifetime`: `one-time` or `task`.
- `invalidators`: the condition or conditions that end the approval.

The only valid lifetime values are `one-time` and `task`. `one-time` is valid for one use. `task` is valid only for the current task and ends at task completion or any recorded invalidator. Unlisted actions or changed targets require new approval. Do not infer an unlisted action: request separate approval. A missing or blank field, changed action or target, any invalidator, task completion, or user withdrawal ends inheritance. A `task` lifetime does not widen the recorded action, target, or constraints.

An explicitly recorded parent elevation grant is inherited only when its non-empty `constraints.command`, `constraints.host`, `constraints.target`, and `constraints.reuse` values are present in a comparable `constraints` object; `constraints.target` must equal `normalized_target`, and all must match exactly. A missing, empty, or changed value requires separate approval. Unrecorded sandbox, credential, and network permissions remain independently gated; user approval does not override a harness denial.

A technical denial is a concrete blocker. Do not retry, elevate, or infer consent automatically; report the blocker and the specific input or approval needed next.

Technical permissions and denials remain independently gated. A handoff does not add authority for issue creation, issue status changes, issue linking, pull requests, or merges; obtain separate approval for each of those actions.

## Commands

Use PowerShell for Git and repository commands in a Windows checkout. The examples here do not require WSL or a Bash shell.

## Closure evidence

The field and status contract is defined by [#29](https://github.com/EltonZhang777/Blasphemous.EditorMode/issues/29); the canonical verification entrypoint is defined by [#31](https://github.com/EltonZhang777/Blasphemous.EditorMode/issues/31). This section does not repeat or invent build, test, whitespace, profile-smoke, or workflow commands.

Every close record identifies the evidence kind, the full 40-character source SHA, the final merged SHA where applicable, the automated status, the runtime status, and evidence links; at least one non-empty evidence link is required. Its delivered scope must be non-empty; when present, every remaining-scope and follow-up-link entry must also be non-empty. Supported evidence kinds are those defined by [#29](https://github.com/EltonZhang777/Blasphemous.EditorMode/issues/29) plus `documentation-only-automated`.

A completed close requires current automated PASS evidence at the exact HEAD and no open remaining scope. Runtime-bearing evidence also requires runtime PASS. `documentation-only-automated` evidence may record runtime `N/A` because it has no runtime surface; `N/A` is not valid for runtime-bearing evidence. Automated failures and zero-test runs are FAIL. Missing, stale, mismatched, incomplete, or unsupported evidence is PARTIAL, never PASS. A later HEAD or changed verification scope invalidates the record and requires re-verification.

An explicit PARTIAL close records the delivered scope, remaining scope, and links to follow-up coverage that already exists. Keep the ticket open when that coverage or issue write permission is unavailable.
