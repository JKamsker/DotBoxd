---
description: |
  Fixes the production issue behind one open [surprise-red-test] PR on the same
  PR branch and folds in CodeRabbit feedback when present. Dispatched per-PR by
  library-surprise-fix-dispatcher; re-proves the red regression test locally
  (red then green) inside this run rather than depending on the approval-gated
  pull_request CI.

on:
  workflow_dispatch:
    inputs:
      pr_number:
        description: The [surprise-red-test] pull request number to fix.
        required: true
        type: string

run-name: "fix #${{ inputs.pr_number }}"

# Per-PR group: distinct fix dispatches for different PRs run in parallel and never
# cancel each other; a duplicate dispatch for the SAME PR queues instead of double-fixing.
concurrency:
  group: surprise-fix-${{ inputs.pr_number }}
  cancel-in-progress: false

permissions:
  actions: read
  contents: read
  issues: read
  pull-requests: read

checkout:
  fetch-depth: 0
  fetch: ["*"]

network:
  allowed:
    - defaults
    - github
    - dotnet
    - threat-detection

sandbox:
  agent:
    targets:
      openai:
        base-url-secret: CODEX_LB_BASE_URL

tools:
  github:
    lockdown: false
    min-integrity: none

safe-outputs:
  mentions: false
  allowed-github-references: []
  push-to-pull-request-branch:
    target: "*"
    required-title-prefix: "[surprise-red-test] "
    required-labels: [bug, ".NET"]
    max: 1
    max-patch-size: 8192
    if-no-changes: "error"
    protected-files: fallback-to-issue
    github-token-for-extra-empty-commit: ${{ secrets.GH_AW_CI_TRIGGER_TOKEN }}
  add-labels:
    target: "*"
    max: 1
  noop:
    report-as-issue: false
  missing-tool: false
  missing-data: false
  report-incomplete:
    create-issue: false

engine:
  id: codex
  model: gpt-5.5
  args:
    - " -c"
    - model_reasoning_effort="high"

pre-agent-steps:
  - name: Resolve eligible surprise fix target
    id: surprise-fix-target
    shell: bash
    env:
      GH_TOKEN: ${{ github.token }}
      EVENT_NAME: ${{ github.event_name }}
      DISPATCH_PR_NUMBER: ${{ inputs.pr_number || '' }}
      SURPRISE_PR_TITLE_PREFIX: "[surprise-red-test] "
      # NOTE: only workflow_dispatch remains; EVENT_NAME kept for the resolver's guard.
    run: |
      set -euo pipefail
      mkdir -p /tmp/gh-aw
      python3 - <<'PY'
      import json
      import os
      import subprocess
      import sys

      event_name = os.environ["EVENT_NAME"]
      repo = os.environ["GITHUB_REPOSITORY"]
      prefix = os.environ["SURPRISE_PR_TITLE_PREFIX"]
      dispatch_pr_number = os.environ.get("DISPATCH_PR_NUMBER", "").strip()
      event_path = os.environ.get("GITHUB_EVENT_PATH")

      with open(event_path, encoding="utf-8") as handle:
          event = json.load(handle)

      target = {
          "should_run": False,
          "reason": "No eligible surprise PR target was found.",
          "event_name": event_name,
          "pr_number": None,
          "pr_url": None,
          "head_ref": None,
          "head_sha": None,
          "ci_proof": None,
      }

      def run_json(args):
          result = subprocess.run(args, check=False, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
          if result.returncode != 0:
              print(result.stderr, file=sys.stderr)
              raise SystemExit(result.returncode)
          return json.loads(result.stdout or "null")

      pr_number = None
      if event_name == "workflow_run":
          workflow_run = event.get("workflow_run") or {}
          if workflow_run.get("event") != "pull_request":
              target["reason"] = "Completed ci run was not for a pull_request event."
          elif workflow_run.get("conclusion") != "failure":
              target["reason"] = f"Completed ci run conclusion was {workflow_run.get('conclusion')!r}, not failure."
          else:
              pull_requests = workflow_run.get("pull_requests") or []
              if len(pull_requests) != 1:
                  target["reason"] = f"Expected one associated pull request, found {len(pull_requests)}."
              else:
                  pr_number = pull_requests[0].get("number")
                  target["ci_proof"] = {
                      "source": "workflow_run",
                      "run_id": workflow_run.get("id"),
                      "run_url": workflow_run.get("html_url"),
                      "head_sha": workflow_run.get("head_sha"),
                      "conclusion": workflow_run.get("conclusion"),
                  }
      elif event_name == "workflow_dispatch":
          if dispatch_pr_number:
              pr_number = dispatch_pr_number
          else:
              target["reason"] = "Manual dispatch did not provide pr_number."
      else:
          target["reason"] = f"Unsupported event: {event_name}"

      if pr_number is not None:
          pr = run_json(
              [
                  "gh",
                  "pr",
                  "view",
                  str(pr_number),
                  "--repo",
                  repo,
                  "--json",
                  "number,title,state,url,headRefName,headRefOid,headRepository,baseRefName,isCrossRepository,labels,commits",
              ]
          )
          label_names = {label.get("name") for label in pr.get("labels") or []}
          head_repository = (pr.get("headRepository") or {}).get("nameWithOwner")
          errors = []
          if pr.get("state") != "OPEN":
              errors.append("PR is not open")
          if not str(pr.get("title") or "").startswith(prefix):
              errors.append(f"PR title does not start with {prefix!r}")
          for required_label in ["bug", ".NET"]:
              if required_label not in label_names:
                  errors.append(f"PR is missing required label {required_label!r}")
          if pr.get("isCrossRepository"):
              errors.append("PR is from a fork or different repository")
          if head_repository and head_repository != repo:
              errors.append(f"PR head repository is {head_repository!r}, expected {repo!r}")
          if pr.get("headRefName") == pr.get("baseRefName"):
              errors.append("PR head branch equals base branch")

          if not target["ci_proof"]:
              runs = run_json(
                  [
                      "gh",
                      "run",
                      "list",
                      "--repo",
                      repo,
                      "--workflow",
                      "ci.yml",
                      "--event",
                      "pull_request",
                      "--limit",
                      "50",
                      "--json",
                      "databaseId,status,conclusion,headSha,url,createdAt,displayTitle",
                  ]
              )
              commit_oids = {
                  commit.get("oid")
                  for commit in pr.get("commits") or []
                  if commit.get("oid")
              }
              commit_oids.add(pr.get("headRefOid"))
              failed_runs = [
                  run
                  for run in runs
                  if run.get("status") == "completed"
                  and run.get("conclusion") == "failure"
                  and run.get("headSha") in commit_oids
              ]
              if failed_runs:
                  run = failed_runs[0]
                  target["ci_proof"] = {
                      "source": "run_list",
                      "run_id": run.get("databaseId"),
                      "run_url": run.get("url"),
                      "head_sha": run.get("headSha"),
                      "conclusion": run.get("conclusion"),
                  }
              else:
                  # A failing pull_request ci run is a nice-to-have, not required:
                  # this run re-proves the red test locally in its own validation
                  # steps (Build + "Verify fix is green" establish red->green here).
                  pass

          if errors:
              target["reason"] = "; ".join(errors)
          else:
              target.update(
                  {
                      "should_run": True,
                      "reason": "Eligible open [surprise-red-test] PR; red is re-proven locally in this run.",
                      "pr_number": pr.get("number"),
                      "pr_url": pr.get("url"),
                      "head_ref": pr.get("headRefName"),
                      "head_sha": pr.get("headRefOid"),
                      "base_ref": pr.get("baseRefName"),
                  }
              )

      with open("/tmp/gh-aw/surprise-fix-target.json", "w", encoding="utf-8") as handle:
          json.dump(target, handle, indent=2, sort_keys=True)

      with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as env_file:
          env_file.write(f"SURPRISE_FIX_SHOULD_RUN={str(target['should_run']).lower()}\n")
          env_file.write(f"SURPRISE_FIX_PR_NUMBER={target.get('pr_number') or ''}\n")
          env_file.write(f"SURPRISE_FIX_PR_URL={target.get('pr_url') or ''}\n")

      with open("/tmp/gh-aw/surprise-fix-target.env", "w", encoding="utf-8") as env_file:
          env_file.write(f"surprise_should_run={str(target['should_run']).lower()}\n")
          env_file.write(f"surprise_pr_number={target.get('pr_number') or ''}\n")

      print(json.dumps(target, indent=2, sort_keys=True))
      PY

      . /tmp/gh-aw/surprise-fix-target.env

      if [ "${surprise_should_run:-false}" = "true" ]; then
        gh pr checkout "$surprise_pr_number" --repo "$GITHUB_REPOSITORY"
        original_head="$(git rev-parse HEAD)"
        echo "SURPRISE_FIX_ORIGINAL_HEAD=${original_head}" >> "$GITHUB_ENV"
      fi

post-steps:
  - name: Check whether fix push validation is required
    id: surprise-fix-guard
    shell: bash
    run: |
      set -euo pipefail
      outputs="/tmp/gh-aw/safeoutputs.jsonl"
      should_validate=false
      if [ -s "$outputs" ] && grep -Eq '"type":"push_to_pull_request_branch"' "$outputs"; then
        should_validate=true
      fi
      echo "should_validate=${should_validate}" >> "$GITHUB_OUTPUT"

  - name: Reject ineligible surprise fix push output
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    shell: bash
    run: |
      set -euo pipefail
      python3 - <<'PY'
      import json
      import os
      import sys

      target_path = "/tmp/gh-aw/surprise-fix-target.json"
      outputs_path = "/tmp/gh-aw/safeoutputs.jsonl"

      with open(target_path, encoding="utf-8") as handle:
          target = json.load(handle)
      if not target.get("should_run"):
          print(f"::error::Refusing push output for ineligible target: {target.get('reason')}", file=sys.stderr)
          sys.exit(1)
      expected_number = int(target["pr_number"])

      push_items = []
      with open(outputs_path, encoding="utf-8") as handle:
          for line_number, line in enumerate(handle, start=1):
              text = line.strip()
              if not text:
                  continue
              item = json.loads(text)
              if item.get("type") == "push_to_pull_request_branch":
                  push_items.append((line_number, item))

      if len(push_items) != 1:
          print(f"::error::Expected exactly one push_to_pull_request_branch output, found {len(push_items)}.", file=sys.stderr)
          sys.exit(1)

      _, item = push_items[0]
      raw_number = (
          item.get("pull_request_number")
          or item.get("pr_number")
          or item.get("pull_number")
          or item.get("pr")
      )
      if int(raw_number) != expected_number:
          print(
              f"::error::Push target PR #{raw_number} does not match resolved target #{expected_number}.",
              file=sys.stderr,
          )
          sys.exit(1)

      print(f"Validated push output target for surprise PR #{expected_number}.")
      PY

  - name: Setup .NET for fix validation
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    uses: actions/setup-dotnet@v4
    with:
      dotnet-version: |
        8.0.x
        9.0.x
        10.0.x

  - name: Materialize safe-output patch for fix validation
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    shell: bash
    run: |
      set -euo pipefail
      original_sha="${SURPRISE_FIX_ORIGINAL_HEAD:-}"
      current_sha="$(git rev-parse HEAD)"
      has_workspace_changes=false
      git diff --quiet || has_workspace_changes=true
      git diff --cached --quiet || has_workspace_changes=true

      if [ "$has_workspace_changes" = false ] && [ -n "$original_sha" ] && [ "$current_sha" = "$original_sha" ]; then
        shopt -s nullglob
        patches=(/tmp/gh-aw/aw-*.patch)
        if [ "${#patches[@]}" -eq 0 ]; then
          echo "::error::Safe output requested a PR branch push, but no workspace changes or patch artifact were found."
          exit 1
        fi
        if [ "${#patches[@]}" -gt 1 ]; then
          printf '::error::Expected one safe-output patch, found %s: %s\n' "${#patches[@]}" "${patches[*]}"
          exit 1
        fi
        git apply --check "${patches[0]}"
        git apply "${patches[0]}"
      fi

      git status --short

  - name: Restore before fix validation
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    run: dotnet restore DotBoxD.slnx

  - name: Build before fix validation
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    run: GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release --no-restore

  - name: Verify fix is green
    if: steps.surprise-fix-guard.outputs.should_validate == 'true'
    run: GITHUB_ACTIONS=true dotnet test DotBoxD.slnx -c Release --no-build
---

# Library Surprise Fix

This workflow repairs an existing `[surprise-red-test]` pull request whose red
regression test reproduces a real bug. It is dispatched per-PR (by number) from
`library-surprise-fix-dispatcher`. Do not discover a new surprise in this
workflow.

The red->green proof lives entirely in this run: you confirm the red test FAILS
on the checked-out branch, implement the fix, and this workflow's post-steps
rebuild and require `dotnet test` to PASS before your push is accepted. It does
not depend on the approval-gated pull_request CI.

## Target Resolution

Read `/tmp/gh-aw/surprise-fix-target.json` first.

If `SURPRISE_FIX_SHOULD_RUN` is not `true`, leave the workspace unchanged and
call `noop` with the recorded reason. This workflow is intentionally selective:
it may only act on an open, same-repository PR whose title starts with
`[surprise-red-test] `, has the `bug` and `.NET` labels, and is not already
marked `sweep:fixed`.

The pre-agent step checks out the target PR branch before you start. Confirm
that the checked-out branch contains the red tests from the PR.

## Required Process

1. Inspect the PR body and diff. Build the branch and run the focused regression
   test; confirm it FAILS for the expected reason — that failure is the proof
   the bug is real. If the test already PASSES, the bug is already fixed: leave
   the workspace unchanged and call `noop`.
2. Inspect CodeRabbit feedback before editing:
   - PR reviews
   - PR review comments
   - PR issue comments
   - current PR checks
   If CodeRabbit is pending, wait briefly and re-check. Address valid
   actionable findings in the same branch. If there are no actionable
   CodeRabbit findings, record that in your final summary.
3. Read the failing tests and nearby production code. Do not remove, skip,
   weaken, or loosen the red regression tests.
4. Implement the smallest maintainable production fix. Keep the public design
   rule intact: public abstractions and generators must remain opt-in sugar over
   public primitives, never lock-in.
5. Run the focused test to verify it is now green, then run:
   `dotnet restore DotBoxD.slnx`,
   `GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release --no-restore`, and
   `GITHUB_ACTIONS=true dotnet test DotBoxD.slnx -c Release --no-build`.
6. Commit the fix on the checked-out PR branch. The commit message must include
   a short summary followed by a body explaining what changed and why.
7. Call `push_to_pull_request_branch` for `SURPRISE_FIX_PR_NUMBER`. Include a
   concise summary covering the confirmed red test, the fix, local validation,
   and CodeRabbit handling. Then call `add_labels` to add `sweep:fixed` to the
   PR so the dispatcher does not re-dispatch it.

If the red test does not fail (already fixed) or the PR is no longer eligible,
leave the workspace unchanged and call `noop`.

## Protected files — never touch them

Never include top-level protected files in your patch: `README.md`, `CONTRIBUTING.md`,
`CHANGELOG.md`, `AGENTS.md`, `CLAUDE.md`, `DESIGN.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`,
`Directory.Packages.props`, `NuGet.Config`, `global.json`, lockfiles, or anything under dot-folders.
The push layer hard-blocks the ENTIRE patch when it contains any of them (your work is discarded
into a "[gh-aw] Protected Files" issue instead of landing). Documented samples and doc pages live
under `docs/**`, which is allowed — put documentation updates there. If a protected file genuinely
must change for correctness, say so in a comment/PR body and leave the file itself untouched for a
human.

Do not print, inspect, or summarize secrets, API keys, virtual tokens, endpoint
hosts, or full endpoint URLs.
