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
    # UTC. Every 30 min at :09/:39 — off-peak minutes; GitHub drops fewer schedule
    # slots away from :00/:05-multiples, and this stays staggered off other crons.
    - cron: "9,39 * * * *"

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
    max: 8
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

      def gh_json(args, tolerant=False):
          # The frontier source (issue list) fails closed: if we cannot read the lenses we must not
          # dispatch. The busy-check (run list) is a best-effort optimization layered over the
          # per-lens `concurrency` lock, so it degrades to "unknown" on error (e.g. the explore
          # workflow has no runs registered yet, or a transient API failure) instead of aborting
          # the whole dispatch tick.
          r = subprocess.run(["gh", *args], check=False, text=True,
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE)
          if r.returncode != 0:
              if tolerant:
                  print(f"warning: gh {' '.join(args)} failed ({r.returncode}): {r.stderr.strip()}")
                  return []
              raise SystemExit(f"gh {' '.join(args)} failed: {r.stderr.strip()}")
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
                      "--json", "status,displayTitle", "--limit", "60"], tolerant=True)
      IN_FLIGHT = ("in_progress", "queued", "requested", "waiting", "pending")
      busy_numbers = set()
      for run in runs:
          if run.get("status") in IN_FLIGHT:
              m = re.search(r"explore #(\d+)", run.get("displayTitle", ""))
              if m:
                  busy_numbers.add(int(m.group(1)))

      # Severity order: security first, codegen last.
      SEV = ["vein:security", "vein:concurrency", "vein:wire", "vein:error-path",
             "vein:resource-lifetime", "vein:roundtrip", "vein:api-contract", "vein:codegen"]
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

For each entry in `frontier.json`, in the given order and up to the cap (8), call the **dedicated**
`library_surprise_explore` safe-output tool **once per entry** — one call per lens. Do **not** use the
generic `dispatch_workflow` tool: in this runtime it is a no-op (it returns success but produces no
collectable safe output, so nothing is dispatched). Pass:

- `lens_issue`: the entry's `lens_issue` (as a string)
- `vein`: the entry's `vein`

If `frontier.json` is empty, call `noop` with the reason "no eligible lenses".

Do not print, inspect, or summarize secrets, API keys, virtual tokens, endpoint hosts, or full
endpoint URLs.
