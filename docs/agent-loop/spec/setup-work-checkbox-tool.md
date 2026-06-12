# 00 - Setup Work: Agent Queue / Checkbox Tool

## Goal

Implement a small repo-local CLI called `agentq`.

`agentq` is the only supported way for Codex agents to append findings, claim work, mark fixes, verify fixes, reject findings, and regenerate human-readable checkbox queues.

Markdown checkboxes are a view, not the source of truth.

```text
source of truth = YAML frontmatter in one finding file per issue
event trail     = one JSONL event file per finding
human view      = generated Markdown queues with checkboxes
```

## Why this exists

Agents are bad at preserving exact formatting in long Markdown lists. A script prevents the workflow from breaking because of bad checkbox syntax, duplicated IDs, stale queue sections, or malformed metadata.

## Implementation location

Prefer:

```text
tools/AgentQueue/
```

For a .NET repo, implement as a .NET console app.

Recommended target:

```text
net10.0 or current repo target
```

If the repository cannot use .NET tooling, implement the same command contract in any language. The command behavior is the important part.

## CLI executable name

During development:

```bash
dotnet run --project tools/AgentQueue -- <command>
```

Optional convenience wrapper later:

```bash
agentq <command>
```

For docs and prompts, use `agentq` as the logical command.

## Setup checklist

### Phase 1 - Repo structure

- [ ] Create `docs/agent-loop/`.
- [ ] Create `docs/agent-loop/findings/`.
- [ ] Create `docs/agent-loop/events/`.
- [ ] Create `docs/agent-loop/queues/`.
- [ ] Create `docs/agent-loop/active/`.
- [ ] Create `docs/agent-loop/README.md`.
- [ ] Create `docs/agent-loop/config.json`.
- [ ] Create `tools/AgentQueue/`.
- [ ] Add the AgentQueue project to the solution if appropriate.
- [ ] Add tests for AgentQueue.

### Phase 2 - Core model

- [ ] Define `Finding`.
- [ ] Define `FindingArea`.
- [ ] Define `FindingStatus`.
- [ ] Define `Priority`.
- [ ] Define `AgentEvent`.
- [ ] Define status transition rules.
- [ ] Define ID generation.
- [ ] Define slug generation.
- [ ] Define frontmatter read/write.
- [ ] Define event JSONL append.

### Phase 3 - File safety

- [ ] Implement repo-root discovery.
- [ ] Implement queue lock acquisition.
- [ ] Implement lock timeout.
- [ ] Implement stale lock diagnostics.
- [ ] Implement atomic file writes.
- [ ] Implement deterministic queue rendering.
- [ ] Implement validation that generated files are not manually edited as source of truth.
- [ ] Implement `agentq doctor`.

### Phase 4 - Commands

- [ ] `agentq init`
- [ ] `agentq append`
- [ ] `agentq list`
- [ ] `agentq next`
- [ ] `agentq claim`
- [ ] `agentq release`
- [ ] `agentq fix`
- [ ] `agentq verify`
- [ ] `agentq reopen`
- [ ] `agentq reject`
- [ ] `agentq duplicate`
- [ ] `agentq obsolete`
- [ ] `agentq note`
- [ ] `agentq render`
- [ ] `agentq doctor`

### Phase 5 - Tests

- [ ] Test ID generation per area.
- [ ] Test duplicate dedup key rejection.
- [ ] Test status transitions.
- [ ] Test invalid transitions fail.
- [ ] Test one finding per file.
- [ ] Test generated queue ordering.
- [ ] Test render is deterministic.
- [ ] Test event JSONL append.
- [ ] Test `doctor` catches malformed frontmatter.
- [ ] Test `doctor` catches queue drift.
- [ ] Test lock prevents concurrent writes.
- [ ] Test conflict-safe behavior when queue files are deleted and regenerated.

### Phase 6 - Codex integration

- [ ] Update root `AGENTS.md` with queue rules.
- [ ] Add agent descriptions under `docs/agent-loop/agents/` or `.codex/agents/`.
- [ ] Add prompts for starting auditors/fixers/verifiers.
- [ ] Add a documented worktree flow.
- [ ] Add a `current-fix.md` template.

## Non-goals for first implementation

Do not implement:

- web dashboard
- GitHub/Jira sync
- automatic benchmark execution framework
- cross-branch locking
- remote database
- auto-merge
- automatic code fixing

Keep v1 local, deterministic, and boring.

## Tool behavior summary

### Append a finding

```bash
agentq append \
  --area correctness \
  --priority high \
  --title "Parser accepts invalid trailing bytes" \
  --dedup-key "correctness/parser/trailing-bytes/final-cursor" \
  --agent correctness-auditor \
  --body-file /tmp/finding.md
```

Creates:

```text
docs/agent-loop/findings/COR-0001-parser-accepts-invalid-trailing-bytes.md
docs/agent-loop/events/COR-0001.jsonl
```

Then regenerates:

```text
docs/agent-loop/queues/correctness.md
```

### Claim next finding

```bash
agentq claim --area correctness --agent fixer --branch fix/COR-0001
```

Output:

```text
CLAIMED COR-0001
file=docs/agent-loop/findings/COR-0001-parser-accepts-invalid-trailing-bytes.md
```

### Mark fixed

```bash
agentq fix COR-0001 \
  --agent fixer \
  --commit "$(git rev-parse HEAD)" \
  --notes "Added regression test and final cursor validation."
```

### Verify / check off

```bash
agentq verify COR-0001 \
  --agent correctness-verifier \
  --commit "$(git rev-parse HEAD)" \
  --cmd "dotnet test -c Release --filter Parser" \
  --notes "Regression test fails before fix and passes after fix."
```

This is the equivalent of checking the checkbox. It sets status to `verified` and renders it as `[x]`.

## Recommended status labels

Use these exact machine values:

```text
open
claimed
fixed_pending_verification
verified
rejected
duplicate
obsolete
```

Generated Markdown may render them as:

```text
[ ] open
[>] claimed
[~] fixed_pending_verification
[x] verified
[-] rejected / duplicate / obsolete
```

## Conflict behavior

The tool should be safe under normal concurrent use inside one worktree. It cannot prevent two separate Git branches from independently adding duplicates. That is acceptable.

Deduplication across branches is handled by:

```bash
agentq dedup scan
```

Optional v2 command.

## Generated queue warning

Every generated queue file must begin with:

```md
<!-- GENERATED BY agentq render. DO NOT EDIT BY HAND. Source of truth: docs/agent-loop/findings/*.md -->
```

If a human edits the queue, `agentq render` may overwrite it.

## Done definition for setup

The setup is done when:

- `agentq init` creates the directory structure.
- `agentq append` creates a valid finding with frontmatter.
- `agentq claim` claims the next open finding.
- `agentq fix` moves a claimed finding to fixed pending verification.
- `agentq verify` moves a fixed finding to verified.
- `agentq render` regenerates all queue Markdown.
- `agentq doctor` validates the queue and reports actionable errors.
- All AgentQueue tests pass.
