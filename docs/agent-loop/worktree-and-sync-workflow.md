# 04 - Worktree and Sync Workflow

## Principle

Use worktrees for isolation, not for long-lived divergent universes.

```text
audit branches can live longer
fix branches should be short-lived
source code fixes should fan in quickly
```

## Recommended worktree layout

From the main repo checkout:

```bash
mkdir -p ../wt

git fetch origin

git worktree add -b audit/completeness ../wt/audit-completeness origin/main
git worktree add -b audit/correctness ../wt/audit-correctness origin/main
git worktree add -b audit/perf-alloc ../wt/audit-perf-alloc origin/main
git worktree add -b audit/perf-algorithm ../wt/audit-perf-algorithm origin/main
```

For a fix:

```bash
git worktree add -b fix/COR-0007 ../wt/fix-COR-0007 origin/main
```

## Auditor branch rule

Auditor branches may only touch:

```text
docs/agent-loop/findings/
docs/agent-loop/events/
docs/agent-loop/queues/
```

They should not edit source code.

## Fix branch rule

A fix branch may touch:

```text
src/
tests/
benchmarks/
docs/agent-loop/findings/<ID>...
docs/agent-loop/events/<ID>.jsonl
docs/agent-loop/queues/<area>.md
```

It should only fix one finding unless explicitly approved.

## Starting an auditor

Example:

```bash
cd ../wt/audit-correctness

codex --sandbox workspace-write --ask-for-approval on-request
```

Prompt:

```text
You are the correctness auditor. Read AGENTS.md, docs/agent-loop/README.md, and agents/correctness-auditor.md if present. Run an audit pass. Use agentq append for new findings. Do not edit production code.
```

## Starting a fixer

```bash
cd ../wt/fix-COR-0007

codex --sandbox workspace-write --ask-for-approval on-request
```

Prompt:

```text
You are the fixer. Read AGENTS.md and docs/agent-loop/findings/COR-0007-*.md. Claim the finding with agentq if not already claimed. Add/adjust tests first where practical. Fix only this finding. Run validation. Mark fixed with agentq fix. Stop after one finding.
```

## Starting a verifier

Prefer a fresh worktree from the fix branch or updated main after merge.

```bash
codex --sandbox workspace-write --ask-for-approval on-request
```

Prompt:

```text
You are the verifier. Read the finding and the fix diff. Do not trust the fix. Try to reproduce the original issue. Run the relevant tests/benchmarks. If valid, run agentq verify. If not, run agentq reopen with a precise reason.
```

## Sync boundaries

Before each new audit pass:

```bash
git fetch origin
git rebase origin/main
agentq render
agentq doctor
```

Before fixing:

```bash
git fetch origin
git rebase origin/main
agentq claim COR-0007 --agent fixer --branch fix/COR-0007
```

Before handoff:

```bash
git status --short
agentq render --check
agentq doctor
dotnet test -c Release
```

Replace `dotnet test -c Release` with repo-specific validation if needed.

## Conflict handling

### Queue Markdown conflict

Generated queue files are not source of truth.

Resolve by:

```bash
git checkout --theirs docs/agent-loop/queues/<area>.md
agentq render
agentq doctor
```

Or simply delete the conflicted queue file and re-render.

### Finding file conflict

A finding file conflict means two agents edited the same finding. Resolve manually. Keep the most recent valid status transition and preserve both useful notes in the event file if possible.

### Duplicate findings across branches

Run:

```bash
agentq doctor
```

If duplicates remain:

```bash
agentq duplicate DUPLICATE_ID --of CANONICAL_ID --agent coordinator --reason "Same issue discovered by another auditor branch."
```

## Cleanup

After merge:

```bash
git worktree remove ../wt/fix-COR-0007
git branch -d fix/COR-0007
git worktree prune
```

For audit branches, either keep them long-running or periodically recreate them from main.

## Strong recommendation

Keep only one active fixer per module/core area.

Safe parallelism:

```text
many auditors
many read-only reviewers
one fixer per isolated module
one verifier per fixed finding
```

Unsafe parallelism:

```text
many agents changing the same public API/core abstraction
many agents fixing perf and correctness in the same files
long-lived fix branches
```
