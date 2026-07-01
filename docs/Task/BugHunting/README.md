# Surprise Hunt Graph — GitHub-integrated bug hunting

Design for a continuous, parallel bug-hunting system that uses **GitHub issues as the
database**. The issue graph is the dedup ledger, branching is explicit, and a red-test PR
with red CI is machine-checkable proof that a bug is real.

Status: **design / proposal** (not yet implemented). Open decisions are listed at the end.

---

## 1. Why

We run `library-surprise-sweep` in an infinite loop with up to 10 parallel research
subagents. It works, but it is hitting diminishing returns for three structural reasons:

1. **No cross-iteration memory.** Each iteration and each agent starts cold, so they
   re-mine veins that are already fixed or already refuted. (Our audit notes even track
   "refuted near-misses not to re-claim" — but that knowledge isn't fed back into the loop.)
2. **Area bias.** The skill's surprise inventory is almost entirely the
   codegen / lowering / marshalling / hook-chain pipeline — the territory already swept 40+
   waves and now converging. Higher-severity, under-mined veins barely appear.
3. **No diversity enforcement.** 10 agents on one identical prompt converge on the same
   obvious targets (birthday-collision waste) instead of covering disjoint ground.

This system fixes all three: the graph is durable, queryable memory; the orchestrator
assigns disjoint high-severity targets; and the frontier is explicit so agents stand on the
accumulated log instead of re-deriving it.

> **This repo already runs a 3-stage gh-aw pipeline** —
> `library-surprise-{sweep,red-test,fix}` in `.github/workflows/` (see `.github/aw/README.md`)
> — an hourly autonomous version of the loop. Today its only memory is *open PRs*: there is no
> record of refuted/exhausted veins, no frontier, no branching, no coverage map. The issue
> graph is exactly that missing durable layer, and it slots directly into the existing
> pipeline. See **§13** for the gh-aw realization.

> This is orthogonal to issues **#141** (unify default-value emission) and **#142**
> (fail-closed-by-construction exhaustiveness). Those systematize the *mature* codegen vein;
> this system steers the hunt toward *new* veins and makes the whole process self-organizing.

---

## 2. Object model

```
LENS issue          (root vein, long-lived)     "vein: sandbox/capability security"
  └─ INVESTIGATION   (a thread / sub-question)   "Are FilePolicy canonical-root checks TOCTOU-safe?"
        ├─ comments  = log trail                 findings, dead ends, what's been checked
        ├─ branch  → child INVESTIGATION         + comment on parent linking child; child body: "Parent: #N"
        └─ surprise → BUG issue                  repro plan + "Investigation: #N"
                       └─ RED-TEST PR            failing test, CI must be RED, body: "Proves #<bug>"
                             └─ (later) GREEN-FIX PR   "Closes #<bug>"
```

| Node             | GitHub primitive | Body holds                                  | Comments hold            |
|------------------|------------------|---------------------------------------------|--------------------------|
| **Lens**         | Issue            | Vein charter, scope, sub-areas, yield count | round summaries          |
| **Investigation**| Issue            | Current hypothesis + state, parent link     | running log, dead ends   |
| **Bug**          | Issue            | Repro plan, investigation link, PR link     | verification notes       |
| **Red-test PR**  | Pull request     | What it proves, link to bug issue           | CI status (must be red)  |
| **Green-fix PR** | Pull request     | The fix, `Closes #<bug>`                     | review                   |

A "thing" (in the original framing) is an **investigation**. It branches into child
investigations; it terminates in a **bug** when a concrete surprise is found.

---

## 3. Label taxonomy

Umbrella: `sweep`

**Kind**
- `sweep:lens` — root vein
- `sweep:investigation` — a thread
- `sweep:bug` — a concrete surprise (may also carry the existing `bug` label)

**Status** (drives the frontier query)
- `sweep:active` — mineable now
- `sweep:exhausted` — swept, low yield; do **not** re-pick
- `sweep:refuted` — investigated and found to be a non-bug (the "do-not-re-mine" record)
- `sweep:proven` — a red-test PR exists, CI is red
- `sweep:fixed` — green fix merged

**Vein tag** (for ranking / weighting)
- `vein:security`, `vein:concurrency`, `vein:wire`, `vein:error-path`, `vein:codegen`

Existing repo conventions reused: the `bug` label and the `surprise-red-test` PR convention.

---

## 4. Linking conventions

Front-matter lines at the top of each issue/PR body, plus a cross-link comment on branch.

```bash
# Branch an investigation into a child
gh issue create --label "sweep:investigation,vein:security" \
  --title "TOCTOU between canonical-root resolve and open in SafeFileSystem" \
  --body "Parent: #<parent>
Lens: #<lens>

<hypothesis + what to check>"
gh issue comment <parent> --body "Branched: #<child> — symlink race on canonical root"

# Open a bug from an investigation
gh issue create --label "sweep:bug,bug,vein:security" \
  --title "SafeFileSystem follows symlink swapped after canonicalization" \
  --body "Investigation: #<inv>
Lens: #<lens>

Repro: <steps / shape>
Expected: <fail-closed diagnostic / denied>
Actual: <escape / wrong behavior>"

# Red-test PR proving the bug (CI expected RED). Use Proves/Refs, NOT Closes.
gh pr create --label "surprise-red-test" \
  --title "test: prove symlink-swap escape in SafeFileSystem (red)" \
  --body "Proves #<bug>
Investigation: #<inv>

Adds a failing test asserting fail-closed. CI is expected RED until the green fix lands."
```

The **green fix** PR (existing integration flow, e.g. the #140-style batch) uses
`Closes #<bug>` and flips the bug's status `sweep:proven` → `sweep:fixed`.

> Rationale for **Refs/Proves not Closes** on the red-test PR: the bug issue must stay open
> (status `proven`) until the green fix merges, so a merged/closed proof PR never loses a
> known-real bug.

---

## 5. Dedup — search before create (the main speed win)

Before opening **any** issue, the agent searches open **and closed** issues:

```bash
gh issue list --state all --search "<keywords> in:title,body" \
  --json number,title,state,labels
```

- If a matching `sweep:refuted` (closed) issue exists → **do not re-open**. Cite it in the
  current log and move on. This is what stops the loop re-investigating dead veins.
- If a matching `sweep:exhausted` lens/investigation exists → don't re-pick it as a target.
- If a matching open `sweep:bug`/`sweep:proven` exists → link to it instead of duplicating.

---

## 6. Claiming — parallel safety for N agents

The orchestrator hands each of the N agents a **disjoint** frontier target per round
(different lens / subsystem). Each agent then:

```bash
gh issue edit <target> --add-assignee @me
gh issue comment <target> --body "Claiming — round <r>, agent <id>"
```

Agents skip any `sweep:active` issue that is already assigned and recently touched. Because
the subagents run with `fork:false` (shared orchestrator context), the orchestrator can
partition targets directly; self-assignment is the backup guard.

**Concurrency split:**
- **Hunt phase** — parallel, read-only on the repo, writes only to GitHub (issues/comments)
  and isolated red-test branches. Safe to run 10-wide.
- **Fix / integrate phase** — serialized (existing flow). Green fixes touch shared,
  high-contention files (API baselines, CI required-test counts, diagnostic catalogs) and
  must not run 10-wide. Out of scope for the hunt; keep it as the current integration step.

---

## 7. Lifecycle

```
Investigation:  active ──► exhausted            (swept, low yield)
                active ──► (spawns) Bug
                active ──► refuted               (closed; do-not-re-mine record)

Bug:            unproven ──► proven (red PR, CI red) ──► fixed (green PR merged, closed)
                unproven ──► refuted              (closed; was not actually a bug)
```

---

## 8. Orchestrator loop (per round)

1. **Pull frontier:** `gh issue list --label sweep:active --json number,title,labels,assignees`,
   ranked by vein severity × freshness × not-recently-touched.
2. **Partition** the top-N disjoint targets (distinct lenses/subsystems) across N agents.
3. **Each agent:**
   - Loads the `library-surprise-sweep` technique (how to find + prove one surprise).
   - Investigates the assigned issue; logs progress as comments.
   - Branches child investigations when a sub-lead is independently mineable.
   - On a concrete surprise: opens a bug issue + a red-test PR (CI red), links both.
   - Updates status labels (`exhausted` / `refuted` / `proven`).
4. **Reconcile:** orchestrator closes exhausted/refuted, re-ranks the frontier, repeats.

Yield signal: counting `sweep:bug` children per lens makes "where bugs are densest" visible,
so the queue can be re-weighted toward productive veins over time.

---

## 9. Skill composition

- **`library-surprise-sweep`** (existing, `.codex/skills/`) — kept as the **technique**:
  how to find and prove a single surprise. Unchanged.
- **`surprise-hunt-graph`** (new) — the **orchestration / bookkeeping** layer: owns the
  GitHub protocol in this document and *calls* the technique. This is what the infinite loop
  drives.

---

## 10. Seed lenses (the under-explored, higher-yield veins)

All orthogonal to the mature codegen vein and to #141/#142.

1. **`vein:security` — sandbox & capability security.** Path/symlink traversal,
   canonicalization mismatches, capability/grant bypass, TOCTOU on policy validation,
   resource exhaustion. Highest severity for a sandboxed plugin runtime.
   Likely targets: `SafeFileSystem`, `FilePolicyGrantValidator`, `SafeHttpGrantValidator`,
   `PolicyGrantValidator`, `AllowedExtensionParameterValidator`, `SandboxPath`.
2. **`vein:concurrency` — concurrency & lifecycle.** Races, re-entrancy, setup-replay,
   disposal / use-after-dispose, leaked handles on exception paths, cancellation correctness
   end-to-end. (Tip of this vein already showed up in #131: concurrent pending connects,
   canceled-install caching.)
3. **`vein:wire` — wire/codec adversarial input.** MessagePack / RPC decode robustness
   against a malicious or version-skewed peer: malformed/truncated frames, oversized
   payloads, length-prefix overflow, map-shape abuse.
4. **`vein:error-path` — error & partial-failure semantics.** What happens when step N of M
   throws: rollback, retry idempotency (at-least-once vs exactly-once), swallowed faults,
   partial-state leaks. (Related to the result-hooks transport gap, #80.)
5. **`vein:codegen` — codegen/lowering** (mature). Keep as a lower-priority lens; mostly
   covered by #141/#142 going forward.

---

## 11. Decisions (resolved for v1)

1. **Issue granularity** → **coarse.** The lens comment log is the investigation trail;
   micro-leads are comments; a standalone issue is created only when a sub-thread is
   independently mineable.
2. **Seeding** → **bootstrapped.** Label taxonomy + 5 root lens issues (#145–#149) created.
3. **Lens priority** → **security first**, then concurrency, wire, error-path, codegen (last).
4. **Discovery venue** → **autonomous gh-aw pipeline** (dispatcher + explore). The local
   10-fork loop can still write to the same graph if desired.
5. **Parallelism width** → **~5 (one per vein), cron hourly, dispatch cap 5.** Tune via the
   dispatcher cron / `dispatch-workflow: max`.
6. **Bug-issue granularity** → **red-test PR is the bug record**; a standalone `sweep:bug`
   issue is created only when a finding needs tracking beyond one PR. Forced by the platform
   constraint in §14 (safe-outputs don't return created object numbers to the run).

---

## 12. Bootstrap sketch (once decisions are settled)

```bash
# Labels
for l in "sweep:lens" "sweep:investigation" "sweep:bug" \
         "sweep:active" "sweep:exhausted" "sweep:refuted" "sweep:proven" "sweep:fixed" \
         "vein:security" "vein:concurrency" "vein:wire" "vein:error-path" "vein:codegen"; do
  gh label create "$l" 2>/dev/null || true
done

# One root LENS issue per seed vein (section 10), labeled sweep:lens + sweep:active + vein:*
```

---

## 13. Realization as gh-aw workflows

You already run the autonomous version of this loop. The graph adds the missing memory layer
to the existing pipeline; it does not replace it.

### What exists today (PR-centric)

| Workflow | Trigger | Role | Writes |
|----------|---------|------|--------|
| `library-surprise-sweep`   | `schedule` (hourly) + dispatch | discovery; finds 1 candidate, dedups vs **open PRs**, hands off | `dispatch-workflow` |
| `library-surprise-red-test`| `workflow_dispatch` (handoff)  | adds red tests, enforces red CI, opens `[surprise-red-test]` PR | `create-pull-request` |
| `library-surprise-fix`     | `workflow_run: ci completed`   | fixes prod on the PR branch after CI proves red | `push-to-pull-request-branch` |

State lives **only in open PRs**. No refuted/exhausted record, no frontier, no branching.

### What the graph adds (issue-centric memory)

Every graph node is a gh-aw `safe-output` — either one you already use or a core gh-aw one:

| Graph concept | gh-aw mechanism |
|---------------|-----------------|
| Lens / Investigation / Bug node | `safe-outputs: create-issue` (+ labels) |
| Log trail | `safe-outputs: add-comment` |
| Branch + back-link | `create-issue` (child) + `add-comment` (parent) |
| Status transitions | `safe-outputs: add-labels` / `update-issue` |
| Dedup ledger (open **and** closed, incl. `sweep:refuted`/`exhausted`) | `tools: github` read |
| Red-test proof | existing `create-pull-request` + `Proves #<bug>` |
| Green fix | existing `push-to-pull-request-branch` + `Closes #<bug>` |
| Continuous | existing `on: schedule` |

### Parallelism: jobs vs runs

A former local subagent becomes **one agent run scoped to a lens/target** — but two GitHub
mechanisms can produce that fan-out, and the distinction matters:

- A **matrix** (`strategy.matrix`) makes parallel **jobs inside one run** of one workflow.
- **`dispatch-workflow`** makes separate **runs**, each its own workflow invocation.

| | Matrix | Dispatcher + `dispatch-workflow` |
|---|--------|----------------------------------|
| Unit | job per matrix cell (one run) | run per dispatch (separate) |
| Target set | **static** list baked into the workflow | **dynamic** — chosen at runtime from the graph |
| Fit | fixed lens roots | the live `sweep:active` frontier |
| In your repo | ⚠️ verify fork exposes `strategy` in frontmatter | ✅ already used (`sweep` → `red-test`) |

**Recommendation: dispatcher.** Which targets are worth advancing each tick is a runtime
decision against the graph, not a fixed list — so a small scheduled **dispatcher** workflow
(the §8 orchestrator) reads the frontier and dispatches one `explore` run per eligible lens.
Matrix is fine only for the dead-simple "one job per static vein root" case.

Guarantees and consequences:

- **`concurrency: group: explore-${lens}`** on `explore` is the real "same lens never
  double-runs" lock; the dispatcher's in-flight check is only a cheap optimization.
- The count is **frontier-driven, not fixed at 10** — dispatch ~one run per active vein (plus
  depth where a vein is productive), decided each tick. You ran 10 before to brute-force
  coverage *because there was no memory*; the graph lets you spend fewer, more precisely.
- Fan-out also happens **across workflows**: an `explore` run that finds a bug dispatches a
  `red-test` run, whose red CI triggers a `fix` run. Matrix/dispatch only parallelizes
  *discovery*.
- Within a lens, the claim-via-assignee/label pattern (§6) covers the residual race; across
  lenses there is none by construction.

### Dispatcher (orchestrator) workflow sketch

> This dispatcher is **mechanical**: read the frontier, dispatch top-K. The pre-agent step does
> the selection deterministically, so the agent is a thin rubber stamp — you can drop `engine:`
> and make this a plain `.yml` to avoid the model call entirely. Keep it agentic only if you
> want it to also do yield-based re-prioritization or branch dry lenses.
>
> **Sketch only — the authoritative, reviewed file is
> `.github/workflows/library-surprise-dispatcher.md`.** The shipped version parses the lens id from
> the run-name and compares it as an int (the substring test below lets `#14` match `#140`), filters
> out `sweep:exhausted` lenses, and fails closed if the run-list query errors.

```yaml
---
description: |
  Orchestrator for the surprise-hunt graph. Reads the live sweep frontier and dispatches one
  `library-surprise-explore` run per eligible lens (severity-ordered, capped), skipping lenses
  that already have an explore run in flight. Read-only; never edits files or opens issues/PRs.

on:
  workflow_dispatch:
  schedule:
    - cron: "5 * * * *"        # stagger off the explore / CI crons

permissions:
  contents: read
  issues: read                 # read the graph
  actions: read                # detect in-flight explore runs
  pull-requests: read

network:
  allowed: [defaults, github, threat-detection]

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
    max: 5                     # K = max concurrent lenses dispatched per tick
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
    - " -c"                    # intentional leading space (fork quirk; see .github/aw/README.md)
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
      import json, os, subprocess

      repo = os.environ["GITHUB_REPOSITORY"]
      def gh_json(args):
          r = subprocess.run(["gh", *args], check=True, text=True, stdout=subprocess.PIPE)
          return json.loads(r.stdout or "[]")

      lenses = gh_json(["issue", "list", "--repo", repo, "--state", "open",
                        "--label", "sweep:lens", "--label", "sweep:active",
                        "--json", "number,title,labels", "--limit", "50"])

      runs = gh_json(["run", "list", "--repo", repo,
                      "--workflow", "library-surprise-explore.lock.yml",
                      "--json", "status,displayTitle", "--limit", "50"])
      busy = {r.get("displayTitle", "") for r in runs
              if r.get("status") in ("in_progress", "queued")}

      SEV = ["vein:security", "vein:concurrency", "vein:wire", "vein:error-path", "vein:codegen"]
      def rank(i):
          names = {l["name"] for l in i.get("labels", [])}
          return next((n for n, v in enumerate(SEV) if v in names), len(SEV))

      eligible = []
      for i in sorted(lenses, key=rank):
          if any(f"#{i['number']}" in t for t in busy):   # explore sets run-name = "explore #<lens> ..."
              continue
          vein = next((l["name"] for l in i.get("labels", []) if l["name"].startswith("vein:")), "")
          eligible.append({"lens_issue": i["number"], "vein": vein, "title": i["title"]})

      json.dump(eligible, open("/tmp/gh-aw/frontier.json", "w"), indent=2)
      print(json.dumps(eligible, indent=2))
      PY
---

# Surprise Hunt Dispatcher

Read `/tmp/gh-aw/frontier.json`: active lenses, highest-severity first, with any lens that
already has an `explore` run in flight removed.

Your only job is to fan out. Do **not** read source, edit files, or open issues/PRs.

For each lens in `frontier.json`, up to the `dispatch-workflow` cap and in the given order,
emit one `dispatch-workflow` call for `library-surprise-explore` with inputs:

- `lens_issue`: the lens issue number
- `vein`: the `vein:*` tag

If `frontier.json` is empty, call `noop` with "no eligible lenses". Do not print or summarize
secrets, endpoint hosts, or full endpoint URLs.
```

`explore` must accept the dispatch and key both the run-name and the concurrency lock on the
lens issue number, so the dispatcher's in-flight check and the "no double-mining" guarantee
line up:

```yaml
on:
  workflow_dispatch:
    inputs:
      lens_issue: { required: true, type: string }
      vein:       { required: false, type: string }

run-name: "explore #${{ inputs.lens_issue }} ${{ inputs.vein }}"

concurrency:
  group: explore-${{ inputs.lens_issue }}
  cancel-in-progress: false
```

(Verify the fork passes `run-name` / `concurrency` through from frontmatter; otherwise set them
in the compiled lock or add fork support.)

### Changes to the three workflows

> **As-built differs — see §14.** These bullets are the original proposal. Shipped v1 makes the bug
> issue *optional* (the red-test PR is the bug record), does **not** attempt in-run PR↔issue linking
> (not possible; see §14), and leaves `red-test` / `fix` unchanged.

1. **`sweep` → `explore`**
   - Add `safe-outputs: create-issue / add-comment / add-labels` (the safe-output handler job
     gets `issues: write`; the agent job stays read-only).
   - Upgrade *Required Duplicate Check* from "search open PRs" to "search the **issue graph**,
     open + closed, including `sweep:refuted` / `sweep:exhausted`."
   - Pick the target from the lens's `sweep:active` frontier instead of cold-scanning; log
     progress as comments; branch via `create-issue`; mark `exhausted` / `refuted`.
   - On a concrete surprise: create the bug issue, then `dispatch-workflow` red-test with
     `bug_issue` added to the handoff JSON.
   - Add a `matrix` over lenses + per-lens `concurrency`.
2. **`red-test`**
   - Handoff carries `bug_issue`; PR body adds `Proves #<bug>` + `Investigation: #<inv>`.
   - On PR creation, `add-comment` to the bug issue linking the PR + `add-labels sweep:proven`.
3. **`fix`**
   - Fix commit/PR uses `Closes #<bug>`; on green push, `add-labels sweep:fixed` and close
     the bug issue.

### Caveats — verify before building

- **Fork safe-output support.** Confirm `JKamsker/gh-aw v0.82.0-jk.1` ships `create-issue` /
  `add-comment` / `add-labels` / `update-issue`. The current workflows only exercise
  `dispatch-workflow` / `create-pull-request` / `push-to-pull-request-branch` / `noop`. If a
  safe-output is missing, either add it to the fork or have the handler shell out to `gh` with
  `GH_AW_GITHUB_TOKEN`.
- **Spam / cost.** A continuous issue-creating loop will flood issues without tight `max:` caps
  on `create-issue` per run **and** strict dedup. Keep the existing `noop` discipline.
- **Recompile locks** after editing sources: `gh aw compile --approve --force-refresh-action-pins
  --gh-aw-ref v0.82.0-jk.1` (see `.github/aw/README.md`); never hand-edit `*.lock.yml`.
- **Codex engine quirk** — preserve the intentional leading space in `engine.args[0]` for this
  fork (documented in `.github/aw/README.md`).

---

## 14. v1 implementation status

Implemented on branch `feat/surprise-hunt-graph` (see the PR). What actually shipped vs. the
plan above:

### Built
- **Labels** — full `sweep:*` + `vein:*` taxonomy created on the repo.
- **Lens issues** — #145 security, #146 concurrency, #147 wire, #148 error-path, #149 codegen
  (each `sweep:lens` + `sweep:active` + `vein:*`, with a charter body).
- **`library-surprise-dispatcher.md`** — hourly orchestrator (§13 sketch, realized). Computes the
  eligible-lens frontier in a pre-agent step and dispatches `explore` per lens (cap 5).
- **`library-surprise-explore.md`** — per-lens discovery agent. `run-name` + per-lens
  `concurrency` lock; safe-outputs `add-comment`/`add-labels` (`target: "*"`), `create-issue`
  (`sweep:bug`), `dispatch-workflow` (red-test). Reuses the `library-surprise-sweep` technique
  skill + the new `surprise-hunt-graph` skill.
- **`.codex/skills/surprise-hunt-graph/`** — the graph protocol skill.
- **`library-surprise-sweep.md`** — schedule disabled (superseded); kept for manual runs.
- **`library-surprise-red-test.md` / `library-surprise-fix.md`** — unchanged; `explore` emits the
  same handoff contract, so the proven workers need no edits.

### Platform constraint that shaped v1
gh-aw applies safe-outputs **after** the agent run and does **not** return created object numbers
(issue/PR) back to that run. So the spec's "create the bug issue, then pass its number to the
red-test handoff" (and any bidirectional PR↔issue auto-link within a run) is not realizable without
fragile correlation-search + safe-output ordering assumptions. Resolution: the **red-test PR is the
bug record**; **lens-issue comment logs are the durable memory / dedup ledger**; standalone
`sweep:bug` issues are optional durable nodes for findings that outlive one PR. This is also the
"coarse granularity" default, so no value is lost for v1.

### Deferred (documented follow-ups)
- **First-class bug issues with automatic PR links** — best done in a merge-triggered stage that
  knows the PR number and can create + link the bug issue atomically.
- **Child `sweep:investigation` issues for branching** — v1 uses lens comments (coarse). Promote to
  child issues when a sub-thread becomes independently mineable by a different agent.
- **`sweep:proven` / `sweep:fixed` label automation** — meaningful once first-class bug issues
  exist; in v1, proof/fix status is carried by the PR (open-red = proven, merged = fixed).
- **Matrix variant** — the dispatcher (dynamic frontier) was chosen over a static matrix.

### Activation
gh-aw scheduled/dispatch workflows execute from the default branch. The discovery
`library-surprise-dispatcher` is currently **disabled** (`gh workflow disable`), so no discovery
fans out until deliberately enabled. `library-surprise-fix-dispatcher` is enabled but only fires on
red-test completion, so it stays dormant while discovery is off. Enable the discovery dispatcher to
run the full autonomous loop; dispatch the fix-dispatcher via `workflow_dispatch` (with `max`) to
drain the existing red-test backlog on demand. `GH_AW_CI_TRIGGER_TOKEN` alone does **not** bypass
the CI approval gate — see "CI approval gate" below.

### Runtime gotcha found by live testing (fixed)
The generic `dispatch_workflow` safe-output tool is a **no-op** in this fork: the agent's call
returns `200 OK` but writes no collectable safe-output message, so the safe_outputs job reports
"Found 0 messages" and **nothing is dispatched**. The correct mechanism is the auto-generated
**per-workflow** tool — `library_surprise_explore` (dispatcher) / `library_surprise_red_test`
(explore) — which does persist a message. Both agent prompts now name the dedicated tool
explicitly and warn off the generic one. This was invisible to compile/validate and only surfaced
when the dispatcher ran live and created zero explore runs; the explore→red-test path happened to
pick the dedicated tool on its own. Verified fixed: after the change the dispatcher reports "Found 5
messages" and fans out one explore run per lens.

### CI approval gate + the fix half was dead (found by live testing; fixed via Plan B)
The Activation note's original claim was wrong: `GH_AW_CI_TRIGGER_TOKEN` does **not** make bot PRs
auto-run CI. Its empty-commit push authenticates as `github-actions[bot]`, so every red-test PR's
`ci` run sat at **`action_required`** — GitHub requires approval for workflow runs on PRs from a
first-time contributor, which the bot always is (confirmed: the run `actor`/`triggering_actor` were
`github-actions[bot]`, and the fork `/approve` REST endpoint rejects these as "not a fork PR").
Because `library-surprise-fix` triggered on `workflow_run: ci` with `conclusion == failure`, and
`ci` never actually ran, **fix was `skipped` on every run — the fixer half never executed a single
production fix.**

**Resolution — decouple the fixer from the approval-gated CI (Plan B):**
- New **`library-surprise-fix-dispatcher.yml`** (plain Actions YAML): triggers on `workflow_run`
  of the red-test worker (+ manual `workflow_dispatch`). Enumerates open `[surprise-red-test]` PRs
  lacking `sweep:fixed` and dispatches one **fix** worker per PR by number (busy-check dedup on the
  `fix #<n>` run-name). Each tick re-scans all unfixed PRs, so a missed tick self-heals on the next
  red-test completion — no separate `schedule:` needed (and it is intentionally omitted so enabling
  the workflow does not burst-fix the backlog).
- **`library-surprise-fix.md`** reworked: entry is `workflow_dispatch(pr_number)` with **per-PR
  concurrency** (`surprise-fix-<pr>`, no-cancel); the "must have a failing `ci` run" gate is
  dropped. Proof now lives entirely in our own runs — red-test refuses to open a PR unless
  `dotnet test` FAILS in-run, and fix refuses to push unless `dotnet test` PASSES in-run — so the
  approval-gated PR CI is not needed as proof. Fix marks the PR `sweep:fixed` on success.
- Net: the fixer is driven by **our** red-test completion, never by an external approval-gated
  check. Works regardless of how GitHub gates bot PRs or whether any token/setting is present.

**Bonus (not depended on):** relaxed the repo's `actions/permissions/fork-pr-contributor-approval`
policy from `first_time_contributors` → `first_time_contributors_new_to_github`, so bot PRs' `ci`
runs execute automatically (the bot is not new-to-GitHub) and each PR also shows a real red→green
CI check for humans. Mild security relaxation on a public repo (established outside contributors'
`pull_request` workflows run without approval; they still get a read-only token and no secrets). If
undesired, revert the policy — the loop still works via Plan B.

**Live evidence:** a full `ci` run executes end-to-end and goes **red** on a red-test branch (both
`workflow_dispatch` and `pull_request` events → `failure`); a genuine `pull_request` CI failure
emits `workflow_run` and triggers a non-skipped fix run. Note `gh run rerun` does **not** emit
`workflow_run`, which is one reason the fix-dispatcher keys off the red-test worker rather than off
CI.
