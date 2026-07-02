---
description: |
  Runs the local library-surprise-sweep discovery phase on a schedule. This
  agent only finds one candidate public-surface surprise and dispatches the
  dedicated red-test worker.

on:
  # SUPERSEDED by library-surprise-dispatcher + library-surprise-explore
  # (see docs/Task/BugHunting/README.md). The hourly schedule is disabled so this
  # legacy single-candidate discovery agent no longer double-dispatches the red-test
  # worker alongside the graph-based dispatcher. Kept for manual/back-compat runs.
  workflow_dispatch:

permissions:
  contents: read
  issues: read
  pull-requests: read
  actions: read

checkout:
  fetch-depth: 0

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
  dispatch-workflow:
    workflows: [library-surprise-red-test]
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
---

# Library Surprise Sweep Discovery

Use the local skill at `.codex/skills/library-surprise-sweep/SKILL.md` for the
discovery part of this run.

Find one actionable "library surprise" in DotBoxD's public surface, then hand
the candidate to the dedicated red-test workflow. A library surprise is behavior
where an API, attribute, generator, analyzer, runtime contract, or documentation
implies support, but the implementation silently skips it, lowers the wrong
shape, misbehaves at runtime, or accepts unsupported input without a clear
diagnostic.

Do not edit repository files in this workflow. Do not create a pull request in
this workflow. This agent specializes in discovery and handoff only.

## Required Duplicate Check

Before selecting a candidate, use the GitHub read tools to inspect open pull
requests. Search broadly enough to catch semantic duplicates, not just exact
titles:

- open PRs with titles beginning `[surprise-red-test]`
- open PRs labeled `bug` or touching nearby test/source areas
- open PRs whose title/body mentions the same API, attribute, diagnostic ID,
  source-generator path, runtime behavior, or failing shape you are considering

If any open PR already covers the same bug or substantially overlapping failing
shape, leave the workspace unchanged and call `noop` with the duplicate PR
number and the candidate bug you skipped.

## Discovery Scope

Choose at most one cohesive candidate per run. Prefer one bug or one tightly
connected class of bugs. Do not bundle unrelated discoveries just because you
found them in the same run.

Map a focused promise surface: public APIs, attributes, source generator entry
points, analyzers, runtime validators, docs, and nearby tests. Build a small
surprise inventory, then select the strongest candidate that is not already
covered by an open PR.

## Handoff Contract

Dispatch `.github/workflows/library-surprise-red-test.md` with these inputs:

- `candidate_title`: a concise, PR-title-ready description that omits the
  `[surprise-red-test] ` prefix.
- `candidate_payload`: compact JSON containing:
  - `bug`: the public promise and the surprising behavior.
  - `expected_red_test`: the user-visible behavior the worker should prove.
  - `expected_failure`: the assertion, diagnostic mismatch, or runtime failure
    expected before the production fix.
  - `suggested_test_area`: the likely test project/files to inspect.
  - `suggested_source_area`: the likely production project/files to inspect.
  - `duplicate_check`: search terms used and open PRs inspected.

If you cannot produce that handoff without guessing, leave the workspace
unchanged and call `noop` with the reason.

Do not print, inspect, or summarize secrets, API keys, virtual tokens, endpoint
hosts, or full endpoint URLs.
