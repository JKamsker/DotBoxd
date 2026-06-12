---
id: <ID>
area: <area>
status: open
priority: <priority>
title: <title>
dedup_key: <dedup-key>
created_at: <utc>
created_by: <agent>
created_commit: <sha>
updated_at: <utc>
claimed_by:
claimed_at:
claim_branch:
fixed_by:
fixed_at:
fixed_commit:
verified_by:
verified_at:
verified_commit:
duplicate_of:
---

# <ID>: <title>

## Claim

State the issue in one or two precise paragraphs.

## Why this matters

Explain user impact, correctness impact, performance impact, or maintainability impact.

## Evidence

Include one or more:

- code path
- minimal reproduction
- failing behavior
- benchmark observation
- static reasoning
- docs/API mismatch

## Suggested test or benchmark

Describe the test/benchmark that should prove the issue and protect the fix.

## Suggested fix direction

Give a small implementation direction. Do not prescribe a giant rewrite unless unavoidable.

## Scope boundaries

What should not be changed while fixing this.

## Deduplication key

`<dedup-key>`

## Verification checklist

- [ ] Reproduction or test exists where practical.
- [ ] Fix addresses root cause.
- [ ] Relevant tests pass.
- [ ] Perf/allocation evidence exists where practical.
- [ ] No unrelated behavior changed.
