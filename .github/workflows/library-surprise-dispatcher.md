---
description: |
  Orchestrator for the surprise-hunt graph. On a schedule it reads the live lens frontier
  (open `sweep:lens` + `sweep:active` issues), drops any lens that already has an explore run in
  flight, severity-orders the rest, and dispatches one `library-surprise-explore` run per eligible
  lens (capped). Read-only against the repo; it never edits files or opens issues/PRs itself.
  See docs/Task/BugHunting/README.md.

on:
  workflow_dispatch:
  schedule:
    # UTC. Hourly at :05, staggered off the CI and legacy sweep crons.
    - cron: "5 * * * *"

permissions:
  contents: read
  issues: read
  pull-requests: read
  actions: read

network:
  allowed:
    - defaults
    - github
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
  dispatch-workflow:
    workflows: [library-surprise-explore]
    max: 5
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
  - name: Compute eligible lenses
    shell: bash
    env:
      GH_TOKEN: ${{ github.token }}
    run: |
      set -euo pipefail
      mkdir -p /tmp/gh-aw
      python3 - <<'PY'
      import json, os, re, subprocess

      repo = os.environ["GITHUB_REPOSITORY"]

      def gh_json(args):
          # Fail closed: a transient gh failure aborts this dispatch tick (it retries next cron)
          # rather than silently emptying the in-flight set and re-dispatching running lenses.
          r = subprocess.run(["gh", *args], check=True, text=True, stdout=subprocess.PIPE)
          return json.loads(r.stdout or "[]")

      # Active lens roots; drop any also marked exhausted (nothing else retires sweep:active,
      # so an exhausted lens would otherwise be re-dispatched every tick).
      lenses = [
          i for i in gh_json(["issue", "list", "--repo", repo, "--state", "open",
                              "--label", "sweep:lens", "--label", "sweep:active",
                              "--json", "number,title,labels", "--limit", "50"])
          if "sweep:exhausted" not in {l["name"] for l in i.get("labels", [])}
      ]

      # Lens numbers with an explore run already in flight -> skip (no double-mining).
      # explore run-name = "explore #<lens_issue> <vein>": parse the number, compare as int
      # (a substring test would let #14 match "#140").
      runs = gh_json(["run", "list", "--repo", repo,
                      "--workflow", "library-surprise-explore.lock.yml",
                      "--json", "status,displayTitle", "--limit", "60"])
      IN_FLIGHT = ("in_progress", "queued", "requested", "waiting", "pending")
      busy_numbers = set()
      for run in runs:
          if run.get("status") in IN_FLIGHT:
              m = re.search(r"explore #(\d+)", run.get("displayTitle", ""))
              if m:
                  busy_numbers.add(int(m.group(1)))

      # Severity order: security first, codegen last.
      SEV = ["vein:security", "vein:concurrency", "vein:wire", "vein:error-path", "vein:codegen"]
      def rank(issue):
          names = {l["name"] for l in issue.get("labels", [])}
          return next((n for n, v in enumerate(SEV) if v in names), len(SEV))

      eligible = []
      for issue in sorted(lenses, key=rank):
          if issue["number"] in busy_numbers:
              continue
          vein = next((l["name"] for l in issue.get("labels", [])
                       if l["name"].startswith("vein:")), "")
          eligible.append({"lens_issue": issue["number"], "vein": vein, "title": issue["title"]})

      with open("/tmp/gh-aw/frontier.json", "w", encoding="utf-8") as f:
          json.dump(eligible, f, indent=2)
      print(json.dumps(eligible, indent=2))
      PY
---

# Surprise Hunt Dispatcher

Read `/tmp/gh-aw/frontier.json`. It is the list of active lenses, highest-severity first, with any
lens that already has an `explore` run in flight removed.

Your only job is to fan out — do **not** read source, edit files, or open issues/PRs.

For each entry in `frontier.json`, in the given order and up to the `dispatch-workflow` cap, emit one
`dispatch-workflow` call for `library-surprise-explore` with inputs:

- `lens_issue`: the entry's `lens_issue` (as a string)
- `vein`: the entry's `vein`

If `frontier.json` is empty, call `noop` with the reason "no eligible lenses".

Do not print, inspect, or summarize secrets, API keys, virtual tokens, endpoint hosts, or full
endpoint URLs.
