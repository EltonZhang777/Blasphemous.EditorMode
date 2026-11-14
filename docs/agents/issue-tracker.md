# Issue tracker: GitHub

Issues and specs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

Use PowerShell for all `gh` operations. Issue reads are summary-first. For the default query, cache schema, explicit refresh path, and authoritative-read rules, see [Issue queries and cache](session-quickref.md#issue-queries-and-cache).

- **Create an issue**: Use a PowerShell here-string:
  ```powershell
  $body = @'
  ...
  '@
  $body | gh issue create --title "..." --body-file -
  ```
  After creation, perform the authoritative summary read required by the linked cache policy.
- **Read an issue**: Use the summary query from [Issue queries and cache](session-quickref.md#issue-queries-and-cache); request only task-specific additional fields.
- **List issues**: `gh issue list --state open --json number,title,state,labels,assignees,updatedAt,url` with appropriate `--label` and `--state` filters.
- **Comment on an issue**: Take the required fresh read, then run `gh issue comment $number --body "..."`.
- **Apply / remove labels**: Take the required fresh read, then run `gh issue edit $number --add-label "..."` / `--remove-label "..."`.
- **Close**: Take the required fresh read, run `gh issue close $number --comment "..."`, then take the required fresh read and use it for verification.

Infer the repo from `git remote -v`; `gh` does this automatically when run inside a clone.

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature requests; `/triage` reads this flag.)_

When set to `yes`, PRs run through the same labels and states as issues, using the `gh pr` equivalents:

- **Read a PR**: `gh pr view $number --json number,title,state,labels,author,updatedAt,url` and `gh pr diff $number` for the diff.
- **List external PRs for triage**: `gh pr list --state open --json number,title,state,labels,author,authorAssociation,updatedAt,url` then keep only `authorAssociation` of `CONTRIBUTOR`, `FIRST_TIME_CONTRIBUTOR`, or `NONE` (drop `OWNER`/`MEMBER`/`COLLABORATOR`).
- **Comment / label / close**: `gh pr comment`, `gh pr edit --add-label`/`--remove-label`, `gh pr close`.

GitHub shares one number space across issues and PRs, so a bare `#42` may be either: resolve with `gh pr view 42` and fall back to the summary issue read in [Issue queries and cache](session-quickref.md#issue-queries-and-cache).

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Use the summary query in [Issue queries and cache](session-quickref.md#issue-queries-and-cache), then request only the specific fields the task needs.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body. `gh issue create --label wayfinder:map`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue (`gh api` on the sub-issues endpoint). Where sub-issues aren't enabled, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: GitHub's **native issue dependencies**, the canonical, UI-visible representation. Add an edge with `gh api --method POST "repos/$owner/$repo/issues/$child/dependencies/blocked_by" -F issue_id=$blockerId`, where `$blockerId` is the blocker's numeric **database id** (`gh api "repos/$owner/$repo/issues/$number" --jq .id`, _not_ the `#number` or `node_id`). GitHub reports `issue_dependencies_summary.blocked_by` (open blockers only, the live gate). Where dependencies aren't available, fall back to a `Blocked by: #<n>, #<n>` line at the top of the child body. A ticket is unblocked when every blocker is closed.
- **Frontier query**: list the map's open children (`gh issue list --state open`, scoped to the map's sub-issues / task list), drop any with an open blocker (`issue_dependencies_summary.blocked_by > 0`, or an open issue in the `Blocked by` line) or an assignee; first in map order wins.
- **Claim**: `gh issue edit $number --add-assignee '@me'`, the session's first write.
- **Resolve**: `gh issue comment $number --body "<answer>"`, then `gh issue close $number`, then append a context pointer (gist + link) to the map's Decisions-so-far.
