---
description: |
  Follows up on [surprise-red-test] PRs after CI has proven the red regression
  tests fail, then fixes the production issue on the same PR branch and folds
  in CodeRabbit feedback when present.

on:
  workflow_run:
    workflows: [ci]
    types: [completed]
  workflow_dispatch:
    inputs:
      pr_number:
        description: Optional [surprise-red-test] pull request number to fix.
        required: false
        type: string

permissions:
  actions: read
  contents: read
  issues: read
  pull-requests: read

if: github.event_name == 'workflow_dispatch' || (github.event.workflow_run.event == 'pull_request' && github.event.workflow_run.conclusion == 'failure')

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
  noop:
    report-as-issue: false
  missing-tool: false
  missing-data: false
  report-incomplete:
    create-issue: false

engine: codex

pre-agent-steps:
  - name: Resolve eligible surprise fix target
    id: surprise-fix-target
    shell: bash
    env:
      GH_TOKEN: ${{ github.token }}
      EVENT_NAME: ${{ github.event_name }}
      DISPATCH_PR_NUMBER: ${{ inputs.pr_number || '' }}
      SURPRISE_PR_TITLE_PREFIX: "[surprise-red-test] "
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
                  errors.append("No completed failing ci run was found for the PR branch")

          if errors:
              target["reason"] = "; ".join(errors)
          else:
              target.update(
                  {
                      "should_run": True,
                      "reason": "Eligible surprise red-test PR with failing CI proof.",
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

This workflow repairs an existing `[surprise-red-test]` pull request after CI
has proved that the red regression tests fail. Do not discover a new surprise
in this workflow.

## Target Resolution

Read `/tmp/gh-aw/surprise-fix-target.json` first.

If `SURPRISE_FIX_SHOULD_RUN` is not `true`, leave the workspace unchanged and
call `noop` with the recorded reason. This workflow is intentionally selective:
it may only act on an open, same-repository PR whose title starts with
`[surprise-red-test] `, has the `bug` and `.NET` labels, and has a completed
failing `ci` run as proof that the red tests expose a real bug.

The pre-agent step checks out the target PR branch before you start. Confirm
that the checked-out branch contains the red tests from the PR.

## Required Process

1. Inspect the PR body, diff, and completed failing `ci` run logs. Confirm that
   the failure is caused by the red regression tests, not infrastructure or an
   unrelated failure.
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
5. Run the focused failing test to verify it is now green, then run:
   `dotnet restore DotBoxD.slnx`,
   `GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release --no-restore`, and
   `GITHUB_ACTIONS=true dotnet test DotBoxD.slnx -c Release --no-build`.
6. Commit the fix on the checked-out PR branch. The commit message must include
   a short summary followed by a body explaining what changed and why.
7. Call `push_to_pull_request_branch` for `SURPRISE_FIX_PR_NUMBER`. Include a
   concise summary covering the CI proof run, the fix, local validation, and
   CodeRabbit handling.

If the completed CI failure is not the red-test proof, or if the PR is no
longer eligible, leave the workspace unchanged and call `noop`.

Do not print, inspect, or summarize secrets, API keys, virtual tokens, endpoint
hosts, or full endpoint URLs.
