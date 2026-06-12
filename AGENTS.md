# AGENTS.md

## Repository Expectations

- Keep changes small and reviewable.
- Prefer maintainable, direct code over clever code.
- Add or update tests for behavior changes.
- Add or update benchmarks or allocation tests for hot-path performance changes where practical.
- Do not claim performance improvements without evidence.
- Do not broaden public API without explaining why.
- Run relevant validation before handoff.

## C# Size Guard

- Non-generated C# files should stay under 300 lines where practical.
- `CodeEnforcer` fails tracked C# files over 350 lines unless they are listed in `tools/CodeEnforcer/code-enforcer.json`.
- Files over 500 lines require both an exclusion and a non-empty justification.
- Folders over 15 tracked C# files must be listed in `tools/CodeEnforcer/code-enforcer.json`; prefer focused subdirectories and namespaces for new code.
- Split large code through composition and focused helper types, not partial classes used only to hide line count.

## Agent Queue Workflow

This repository uses `agentq` for multi-agent audit/fix/verify work.

Rules:

- Do not manually edit generated files under `docs/agent-loop/queues/`.
- Do not manually change finding statuses in Markdown.
- Use `agentq append` to create findings.
- Use `agentq claim` before fixing a finding.
- Use `agentq fix` after implementing a fix.
- Use `agentq verify` to close/check off a finding.
- A fixer must not verify its own fix.
- Auditors must not edit production source code.
- Verifiers should not implement fixes unless explicitly assigned.
- Prefer one finding per PR/commit sequence.
- For behavior changes, add or update tests.
- For hot-path performance changes, add or update benchmarks/allocation tests where practical.
- Do not claim performance improvements without evidence.
- Run `agentq doctor` before handoff if you touched the queue.

Source of truth:

- `docs/agent-loop/findings/*.md`
- `docs/agent-loop/events/*.jsonl`

Generated views:

- `docs/agent-loop/queues/*.md`
