# 01 - AgentQueue Tool Spec

## Name

Logical command name:

```text
agentq
```

## Storage model

### Source of truth

Each finding is one Markdown file with YAML-like frontmatter:

```text
docs/agent-loop/findings/<ID>-<slug>.md
```

Example:

```md
---
id: COR-0007
area: correctness
status: open
priority: high
title: Parser accepts invalid trailing bytes
dedup_key: correctness/parser/trailing-bytes/final-cursor
created_at: 2026-06-12T10:00:00Z
created_by: correctness-auditor
created_commit: abc123
updated_at: 2026-06-12T10:00:00Z
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

# COR-0007: Parser accepts invalid trailing bytes

## Claim

The parser accepts inputs with trailing bytes after a complete message.

## Evidence

...

## Suggested test

...

## Suggested fix direction

...
```

The parser only needs to support simple scalar frontmatter:

```text
key: value
```

Do not require a full YAML parser unless the repo already has one.

### Event trail

Each finding has its own event file:

```text
docs/agent-loop/events/<ID>.jsonl
```

Example lines:

```json
{"at":"2026-06-12T10:00:00Z","agent":"correctness-auditor","type":"created","status":"open","commit":"abc123","message":"Created finding"}
{"at":"2026-06-12T11:00:00Z","agent":"fixer","type":"claimed","status":"claimed","commit":"def456","message":"Claimed on branch fix/COR-0007"}
{"at":"2026-06-12T12:00:00Z","agent":"verifier","type":"verified","status":"verified","commit":"123abc","message":"Regression test passes"}
```

Use JSONL because appending is simple and merge conflicts are localized per finding.

### Generated queues

Human-readable generated files:

```text
docs/agent-loop/queues/completeness.md
docs/agent-loop/queues/correctness.md
docs/agent-loop/queues/perf-alloc.md
docs/agent-loop/queues/perf-algorithm.md
docs/agent-loop/queues/api-coherence.md
docs/agent-loop/queues/test-coverage.md
```

These are not authoritative.

## Areas

Use these machine names and ID prefixes:

| Area | Prefix | Queue file |
|---|---|---|
| `completeness` | `CMP` | `completeness.md` |
| `correctness` | `COR` | `correctness.md` |
| `perf_alloc` | `PAL` | `perf-alloc.md` |
| `perf_algorithm` | `ALG` | `perf-algorithm.md` |
| `api_coherence` | `API` | `api-coherence.md` |
| `test_coverage` | `TST` | `test-coverage.md` |

## Priorities

Use:

```text
critical
high
medium
low
idea
```

Meaning:

| Priority | Meaning |
|---|---|
| `critical` | Data loss, security, severe correctness, impossible to ship |
| `high` | Important behavior/perf issue worth fixing soon |
| `medium` | Real issue, normal queue |
| `low` | Minor but valid |
| `idea` | Needs validation before becoming real work |

## Statuses

Use exact machine values:

```text
open
claimed
fixed_pending_verification
verified
rejected
duplicate
obsolete
```

## Status transitions

Allowed:

```text
open -> claimed
open -> rejected
open -> duplicate
open -> obsolete

claimed -> open
claimed -> fixed_pending_verification
claimed -> rejected
claimed -> duplicate
claimed -> obsolete

fixed_pending_verification -> verified
fixed_pending_verification -> open
fixed_pending_verification -> rejected
fixed_pending_verification -> duplicate
fixed_pending_verification -> obsolete

verified -> open only with --force
rejected -> open only with --force
duplicate -> open only with --force
obsolete -> open only with --force
```

Command mapping:

| Command | Transition |
|---|---|
| `append` | create `open` |
| `claim` | `open -> claimed` |
| `release` | `claimed -> open` |
| `fix` | `claimed -> fixed_pending_verification` |
| `verify` | `fixed_pending_verification -> verified` |
| `reopen` | `fixed_pending_verification -> open` |
| `reject` | any non-final -> `rejected` |
| `duplicate` | any non-final -> `duplicate` |
| `obsolete` | any non-final -> `obsolete` |

## ID generation

IDs are monotonically increasing per area prefix.

Algorithm:

1. Scan `docs/agent-loop/findings/*.md`.
2. Find IDs matching prefix, e.g. `COR-\d+`.
3. Choose max + 1.
4. Format as four digits initially: `COR-0001`.
5. If the repo eventually exceeds 9999 findings, allow five+ digits.

No central counter file. Central counters cause merge conflicts.

## Slug generation

Input title:

```text
Parser accepts invalid trailing bytes
```

Slug:

```text
parser-accepts-invalid-trailing-bytes
```

Rules:

- lower case
- ASCII letters/digits only where practical
- replace whitespace and punctuation with `-`
- collapse repeated `-`
- trim leading/trailing `-`
- max 80 chars
- if empty, use `finding`

Filename:

```text
COR-0007-parser-accepts-invalid-trailing-bytes.md
```

## Deduplication

Each finding requires a `dedup_key`.

Example:

```text
correctness/parser/trailing-bytes/final-cursor
alloc/packet-decoder/toarray
algorithm/cache/repeated-linear-scan
```

On append:

1. Scan all existing finding files.
2. If an active or final finding has the same dedup key, fail with non-zero exit code.
3. Print the existing finding ID and file path.

Do not allow empty dedup keys except with `--allow-missing-dedup-key`, which should be forbidden in AGENTS.md.

## Commands

### `agentq init`

Creates directories and config.

Options:

```text
--force
```

Behavior:

- create missing directories
- do not overwrite existing findings
- create README/config if missing
- render queues

### `agentq append`

Required:

```text
--area <area>
--priority <priority>
--title <title>
--dedup-key <key>
--agent <agent-name>
--body-file <path>
```

Optional:

```text
--commit <sha>
--json
```

Behavior:

- validate area/priority
- validate dedup key uniqueness
- generate ID
- create finding file
- create event file
- render affected queue

### `agentq list`

Options:

```text
--area <area>
--status <status>
--priority <priority>
--json
```

Default sort:

1. priority: critical, high, medium, low, idea
2. status order: open, claimed, fixed_pending_verification, verified, rejected, duplicate, obsolete
3. created_at ascending
4. ID ascending

### `agentq next`

Options:

```text
--area <area>
--json
```

Returns the next open finding using the default sort.

### `agentq claim`

Either:

```bash
agentq claim COR-0007 --agent fixer --branch fix/COR-0007
```

Or:

```bash
agentq claim --area correctness --agent fixer --branch fix/COR-0007
```

If no ID is given, claim the next open item in the area.

Required:

```text
--agent
```

Optional:

```text
--branch
--commit
--json
```

Behavior:

- fail if finding is not open
- set claimed metadata
- append event
- render queue
- print claimed ID and file path

### `agentq release`

```bash
agentq release COR-0007 --agent fixer --reason "Blocked by public API decision"
```

Behavior:

- only allowed from `claimed`
- clears claim fields
- sets status `open`
- appends event
- renders queue

### `agentq fix`

```bash
agentq fix COR-0007 \
  --agent fixer \
  --commit abc123 \
  --notes "Added regression test and final cursor validation"
```

Behavior:

- only allowed from `claimed`
- set status `fixed_pending_verification`
- set fixed metadata
- append event
- render queue

### `agentq verify`

```bash
agentq verify COR-0007 \
  --agent correctness-verifier \
  --commit abc123 \
  --cmd "dotnet test -c Release --filter Parser" \
  --notes "Regression test passes"
```

Behavior:

- only allowed from `fixed_pending_verification`
- set status `verified`
- set verified metadata
- append event with command and notes
- render queue

This is the canonical "check off checkbox" command.

### `agentq reopen`

```bash
agentq reopen COR-0007 \
  --agent correctness-verifier \
  --reason "Still accepts trailing NUL byte"
```

Behavior:

- allowed from `fixed_pending_verification`
- set status `open`
- clear fixed/verified metadata? Keep fixed metadata by default, but add event explaining reopen.
- render queue

### `agentq reject`

```bash
agentq reject COR-0007 \
  --agent coordinator \
  --reason "Not a bug; documented behavior"
```

### `agentq duplicate`

```bash
agentq duplicate COR-0008 \
  --of COR-0007 \
  --agent coordinator \
  --reason "Same root cause"
```

Behavior:

- set status `duplicate`
- set `duplicate_of`
- append event
- render queue

### `agentq obsolete`

```bash
agentq obsolete COR-0007 \
  --agent coordinator \
  --reason "Code path removed"
```

### `agentq note`

```bash
agentq note COR-0007 \
  --agent perf-alloc-auditor \
  --message "Also impacts ParseMany benchmark."
```

Behavior:

- append event only
- optionally append to a `## Notes` section in the Markdown body if `--write-body` is passed

### `agentq render`

Options:

```text
--area <area>
--check
```

Behavior:

- regenerate queue Markdown
- with `--check`, fail if generated output would differ

### `agentq doctor`

Validates:

- directory structure exists
- all findings have required frontmatter
- IDs match filenames
- dedup keys are unique
- statuses are valid
- transitions in events are plausible
- queue files match render output
- no generated queue is missing the generated warning
- no finding body is empty
- no final finding lacks reason/evidence fields where required

### Optional v2: `agentq dedup scan`

Find near-duplicates by:

- same title normalized
- same dedup key
- same related file + similar claim
- same suggested test

Do not implement in v1 unless easy.

## Locking

Use a repo-local lock:

```text
docs/agent-loop/.agentq.lock
```

Recommended behavior:

- acquire by creating/opening lock with exclusive access
- include PID, hostname, command, timestamp in lock content where possible
- timeout after 30 seconds by default
- print actionable error if lock cannot be acquired
- allow `--lock-timeout <seconds>`

The lock only protects one worktree. It does not coordinate independent Git branches. That is fine.

## Atomic writes

For all files:

1. Write to temp file in same directory.
2. Flush.
3. Replace or rename atomically.
4. Never leave partial files behind unless process is killed at the wrong moment.

## Exit codes

Use:

| Code | Meaning |
|---:|---|
| 0 | success |
| 1 | user/input error |
| 2 | validation/doctor error |
| 3 | duplicate finding |
| 4 | invalid status transition |
| 5 | lock timeout |
| 10 | unexpected internal error |

## Output format

Default human-readable output must be stable enough for agents.

Examples:

```text
CREATED COR-0007
file=docs/agent-loop/findings/COR-0007-parser-accepts-invalid-trailing-bytes.md
queue=docs/agent-loop/queues/correctness.md
```

```text
CLAIMED COR-0007
file=docs/agent-loop/findings/COR-0007-parser-accepts-invalid-trailing-bytes.md
```

For scripts, support `--json`.

## Generated queue format

Example:

```md
<!-- GENERATED BY agentq render. DO NOT EDIT BY HAND. Source of truth: docs/agent-loop/findings/*.md -->

# Correctness Queue

## Open

- [ ] `COR-0007` high - Parser accepts invalid trailing bytes
  - File: `docs/agent-loop/findings/COR-0007-parser-accepts-invalid-trailing-bytes.md`
  - Dedup: `correctness/parser/trailing-bytes/final-cursor`

## Claimed

- [>] `COR-0008` medium - Overflow in size calculation
  - Owner: fixer
  - Branch: fix/COR-0008

## Fixed pending verification

- [~] `COR-0009` high - Invalid UTF-8 path differs from docs

## Verified

- [x] `COR-0001` high - EOF handling

## Rejected / duplicate / obsolete

- [-] `COR-0002` low - Legacy behavior intentionally preserved
```

## Security / safety

The tool must not:

- run arbitrary commands except recording `--cmd` strings
- access network
- modify files outside `docs/agent-loop` except when explicitly updating AGENTS.md during setup
- auto-merge branches
- auto-delete worktrees
