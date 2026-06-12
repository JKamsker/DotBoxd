# Prompt: Initial Setup

Use this in the main repo checkout.

```text
Read AGENTS.md if present. Then implement the AgentQueue workflow described in the attached plan files:

- 00_SETUP_WORK_CHECKBOX_TOOL.md
- 01_AGENT_QUEUE_TOOL_SPEC.md
- 02_AGENT_QUEUE_TEST_PLAN.md
- 03_REPO_FILES_TO_ADD.md

Do not audit or fix library code yet.

Implement a repo-local CLI named agentq under tools/AgentQueue.

Done when:
- agentq init works
- agentq append works
- agentq claim works
- agentq fix works
- agentq verify works
- agentq render works
- agentq doctor works
- unit tests for the tool pass
- root AGENTS.md includes the agent queue workflow rules
- generated queue files are clearly marked as generated
```
```
