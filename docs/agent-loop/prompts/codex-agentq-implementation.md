# Prompt: AgentQueue Implementation

```text
You are implementing the AgentQueue CLI.

Read:
- 00_SETUP_WORK_CHECKBOX_TOOL.md
- 01_AGENT_QUEUE_TOOL_SPEC.md
- 02_AGENT_QUEUE_TEST_PLAN.md

Implement the smallest robust v1.

Constraints:
- Do not add unnecessary dependencies.
- Prefer simple controlled frontmatter parsing over full YAML unless the repo already uses a YAML parser.
- Use one finding file per issue.
- Use one event JSONL file per issue.
- Use generated queue Markdown as a view, not source of truth.
- Implement locking and atomic writes.
- Implement deterministic rendering.
- Add tests for transitions, deduplication, rendering, and doctor.

Do not edit production library code.
```
```
