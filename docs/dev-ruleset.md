# Dev required-check ruleset

Status: prepared only. Do not activate until every preflight item and the
explicit maintainer approval are recorded in #35.

The exact payload is
[`.github/rulesets/dev-required-check.json`](../.github/rulesets/dev-required-check.json).
It targets only `refs/heads/dev`, requires
`Trusted verification evidence`, accepts that check only from the GitHub
Actions app (`integration_id` 15368), enables strict up-to-date checking, and
has no bypass actors.

## Prepared baseline

On 2026-11-14 the repository reported no rulesets
(`GET /repos/EltonZhang777/Blasphemous.EditorMode/rulesets` returned `[]`).
`GET /repos/EltonZhang777/Blasphemous.EditorMode/branches/dev/protection`
returned HTTP 404, `Branch not protected`. There is therefore no earlier
ruleset or branch-protection configuration to restore.

The fixed check is written by the trusted publisher in
`.github/workflows/publish-pr.yml`. The unprivileged verification workflow has
no check-write permission.

## Preflight

Before activation, the maintainer must provide explicit approval in #35 and
record evidence that:

1. A same-repository pull request received the marker comment and a successful
   `Trusted verification evidence` check.
2. A fork pull request received the same published evidence.
3. A commit merged to `dev` received final `dev-automated` evidence.
4. `gh api apps/github-actions --jq .id` still returns `15368`.
5. The ruleset list still contains no conflicting `dev` ruleset.
6. `GET /repos/EltonZhang777/Blasphemous.EditorMode/branches/dev/protection`
   still returns HTTP 404. Any new protection setting invalidates this rollback
   baseline.

If any item is missing, activation remains blocked.

## Activation

Run these commands only after the approval is linked in #35:

```powershell
gh api repos/EltonZhang777/Blasphemous.EditorMode/rulesets
gh api repos/EltonZhang777/Blasphemous.EditorMode/branches/dev/protection
gh api apps/github-actions --jq .id
Get-Content -Raw .github/rulesets/dev-required-check.json | ConvertFrom-Json | Out-Null
gh api --method POST `
  -H "Accept: application/vnd.github+json" `
  repos/EltonZhang777/Blasphemous.EditorMode/rulesets `
  --input .github/rulesets/dev-required-check.json
```

Record the returned ruleset ID, activation timestamp, approval link, check
context, and preflight evidence in #35. Read the ruleset back by ID and confirm
the name, target, enforcement, empty bypass list, and exact check source.

## Manual audit

After activation, record each PR number and observed result in #35:

1. A deliberately failing-test PR cannot merge and shows a failed
   `Trusted verification evidence` check.
2. A zero-executed-test PR cannot merge and shows the same failed check.
3. A valid PR with a published marker comment can merge normally.
4. The resulting `dev` run publishes final evidence and performs no automatic
   revert.

The two negative cases must be open, non-draft PRs that otherwise satisfy the
branch requirements. Record the failed check and the refused merge action.
Do not keep a temporary bypass path or use draft/closed PRs as proof that
merging is blocked.

## Rollback

The rollback removes the created ruleset and returns to the recorded
unprotected baseline. It is a separate externally visible action: obtain and
link explicit maintainer approval in #35 before running it.

```powershell
gh api --method DELETE `
  repos/EltonZhang777/Blasphemous.EditorMode/rulesets/<ruleset-id>
gh api repos/EltonZhang777/Blasphemous.EditorMode/rulesets
```

Confirm the ruleset ID is absent and record the rollback state in #35. If a
ruleset already existed at activation time, stop: this procedure no longer
matches the prepared baseline and requires a new rollback plan.
