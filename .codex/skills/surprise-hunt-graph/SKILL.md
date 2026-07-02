---
name: surprise-hunt-graph
description: GitHub-issue-graph protocol for a continuous, lens-sharded bug hunt. Use inside the dispatcher/explore workflows to record findings and coordinate parallel agents through GitHub issues — read the lens frontier, dedup against the graph and open PRs, log the investigation trail as lens comments, dispatch the red-test proof, and mark lens status. Pairs with the library-surprise-sweep technique skill, which finds and proves a single surprise.
---

# Surprise Hunt Graph

Operational protocol for the continuous surprise hunt. Full design and rationale:
`docs/Task/BugHunting/README.md`. This skill is the agent-facing "how to behave in the graph"
companion to `library-surprise-sweep` (the technique for finding + proving one surprise).

## Model (v1)

- **Lens issue** (`sweep:lens` + `sweep:active` + `vein:*`): a root vein. Its **comment log is the
  durable investigation trail and dedup ledger** for that vein across every run.
- **Bug record** = the `[surprise-red-test]` PR (the proven pipeline). A standalone `sweep:bug`
  issue is created ONLY when a finding needs tracking beyond one PR (not yet reducible to a single
  red test, or a cluster of related surprises).
- **Proof** = a red-test PR whose CI is red. **Fix** = the same PR turned green by
  `library-surprise-fix`.

> Why the PR is the bug node: gh-aw applies safe-outputs *after* the agent run and does not return
> created object numbers to that run, so bidirectional PR↔issue auto-linking inside one run is not
> possible. Reference findings by candidate title + PR/issue search, never by a pre-known number.

## Every explore run (assigned one lens via `lens_issue`)

1. Read this skill and the `library-surprise-sweep` technique skill.
2. Read the assigned lens issue: the charter (body) **and all comments** — what prior runs already
   checked, found, refuted, or exhausted.
3. **Dedup — search before acting** (open AND closed):
   - `[surprise-red-test]` PRs — **open and merged/closed** (`gh pr list --state all`) — and PRs
     labeled `bug`
   - `sweep:bug` issues (open or closed)
   - the lens comment log — every candidate previously dispatched from this lens and every lead
     previously **refuted** here (in the coarse model, refuted leads live in the comment trail, not
     as separate issues)
   Treat a candidate as covered only if a `[surprise-red-test]` PR (open OR merged/closed) or a
   `sweep:bug` issue exists for it — the PR/issue is the authority, NOT the bare "dispatched" log
   line. If the trail shows a candidate was dispatched but no matching PR exists (a cancelled or
   failed red-test), re-dispatching it is correct. Never re-mine a refuted lead.
4. Pick ONE cohesive target inside the lens's scope: a charter sub-area not yet swept, or continue
   an open thread from the comments.
5. Investigate read-only. You may restore/build to confirm a lead. Never edit files and never open a
   PR in this run — discovery and handoff only.
6. **Log a concise progress comment on the lens issue** (`add-comment` → the lens number): what you
   checked, promising leads, dead ends, any **refuted** lead (so it is not re-mined), and — if you
   dispatched a red-test — the exact `candidate_title` and failing shape. This comment is the durable
   dedup ledger: a red-test PR may be merged and closed later, so the candidate MUST be recorded here
   or a future run will re-dispatch it.
7. Outcome:
   - **Concrete, non-duplicate surprise** → produce the red-test handoff and `dispatch-workflow`
     `library-surprise-red-test`. Put `lens_issue` in the handoff payload. If the finding warrants
     standalone tracking, also `create-issue` (`sweep:bug`) with a repro and a `Lens: #<lens_issue>`
     line (the lens carries the `vein:*` tag).
   - **Sub-area swept dry** → say so in the comment. If the WHOLE lens is exhausted, `add-labels`
     `sweep:exhausted` on the lens.
   - **Nothing actionable** → still leave the progress comment, then `noop` with the reason.

## Rules

- One cohesive candidate per run (mirror the technique skill's discovery discipline).
- Coarse granularity: the lens comment log is the investigation trail; branch a standalone issue
  only when a sub-thread is independently mineable by a different agent.
- Never weaken a guardrail to force a finding — prefer `noop` over a speculative dispatch.
- Structural claiming: each explore run is scoped to one lens and serialized by a per-lens
  concurrency group, so parallel runs across lenses cannot collide. Still dedup within the lens.
- Never print secrets, endpoint hosts, or full endpoint URLs.
