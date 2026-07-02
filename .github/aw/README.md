# Agentic Workflows in DotBoxD

This repository uses `gh aw` to compile agentic workflow source files into
GitHub Actions workflow locks.

We use the DotBoxD fork of `gh-aw`, not the upstream GitHub release:

https://github.com/JKamsker/gh-aw

The pinned fork release is `v0.82.0-jk.1`. This fork carries the secret-backed
OpenAI-compatible endpoint support DotBoxD needs for Codex workflows.

## File Layout

- `.github/workflows/*.md` are the source files humans edit.
- `.github/workflows/*.lock.yml` are generated GitHub Actions workflows. Do not
  hand-edit these except to inspect generated output during review.
- `.github/aw/actions-lock.json` records action pins used by compiled workflows.

The currently compiled agentic workflows are:

- `.github/workflows/gh-aw-smoke-test.md`
- `.github/workflows/library-surprise-sweep.md`
- `.github/workflows/library-surprise-red-test.md`
- `.github/workflows/library-surprise-fix.md`

## Local Setup

Install the forked extension:

```powershell
gh extension remove gh-aw
gh extension install JKamsker/gh-aw --pin v0.82.0-jk.1
gh aw version
```

Expected version output:

```text
gh aw version v0.82.0-jk.1
```

## Editing Workflow Sources

Edit the `.md` source workflow, then regenerate the lock files:

```powershell
gh aw compile --approve --force-refresh-action-pins --gh-aw-ref v0.82.0-jk.1
```

The forked compiler should emit setup actions pinned to this repository:

```yaml
uses: JKamsker/gh-aw/actions/setup@<commit-sha> # v0.82.0-jk.1
```

It should not emit `github/gh-aw-actions/setup` for these workflows.

## Codex Model

DotBoxD's Codex workflows pin `gpt-5.5` in source and run with high reasoning
effort:

```yaml
engine:
  id: codex
  model: gpt-5.5
  args:
    - " -c"
    - model_reasoning_effort="high"
```

The leading space in the first `engine.args` entry is intentional for
`JKamsker/gh-aw@v0.82.0-jk.1`. That compiler appends custom Codex args directly
after the structured-output path in threat-detection runs; without the explicit
separator it generates `detection_result.json-c ...`. Remove the leading space
only after the fork emits that separator itself.

## Custom Endpoint and Tokens

DotBoxD routes Codex through a secret-backed OpenAI-compatible endpoint. Source
workflows should declare only the secret name:

```yaml
sandbox:
  agent:
    targets:
      openai:
        base-url-secret: CODEX_LB_BASE_URL
```

The compiled workflow reads `${{ secrets.CODEX_LB_BASE_URL }}` on the runner,
patches the AWF OpenAI target at runtime, and excludes `CODEX_LB_BASE_URL` from
the agent sandbox environment. Do not print, inspect, or summarize the secret
value.

Custom API tokens should also be passed by secret-backed environment variables,
for example:

```yaml
engine:
  id: codex
  env:
    OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
```

Use the repository secret name that matches the target workflow. Never commit
token values.

For GitHub write operations, gh-aw safe outputs keep the agent job read-only and
perform the write in a separate handler job. DotBoxD relies on the standard
gh-aw token secret names:

- `GH_AW_GITHUB_TOKEN` can override the default GitHub token used by safe-output
  GitHub operations.
- `GH_AW_CI_TRIGGER_TOKEN` should be a repository-scoped token with Contents
  read/write permission. gh-aw uses it for the extra empty commit that causes PR
  CI to run after `create-pull-request` and `push-to-pull-request-branch`
  outputs.

## Library Surprise Automation

The library-surprise automation is intentionally split across specialized
agent runs:

1. `.github/workflows/library-surprise-sweep.md` is the discovery agent. It
   finds one candidate surprise, performs duplicate checks, and dispatches the
   red-test worker with a compact handoff. It does not edit files.
2. `.github/workflows/library-surprise-red-test.md` is the proof agent. It adds
   only red regression tests, verifies the branch builds and the tests fail
   locally, then creates a `[surprise-red-test]` PR.
3. Repository CI runs on that PR and proves the bug exists by failing on the red
   tests.
4. `.github/workflows/library-surprise-fix.md` reacts to the failed `ci` run for
   an eligible `[surprise-red-test]` PR, checks out the same PR branch, fixes the
   production issue, addresses actionable CodeRabbit feedback, validates the
   full build/test suite, and pushes the fix back to the PR branch.
5. The configured `GH_AW_CI_TRIGGER_TOKEN` path causes CI to run again after the
   fix push.

## Validation

After regeneration, run:

```powershell
gh aw compile --no-emit --validate --approve
git diff --check
```

For changes that affect CI behavior, also run the usual repository validation:

```powershell
dotnet format whitespace DotBoxD.slnx --verify-no-changes --no-restore
$env:GITHUB_ACTIONS='true'; dotnet build DotBoxD.slnx -c Release
dotnet test DotBoxD.slnx -c Release --no-build
```

If `gh aw` reports safe-update changes for new actions, secrets, or redirects,
review them explicitly in the PR description.
