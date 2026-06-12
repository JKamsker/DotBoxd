# Prompt: Start Auditor Agents

Use from main/coordinator when asking Codex to spawn subagents.

```text
Spawn specialized auditor subagents. Each auditor must read AGENTS.md and docs/agent-loop first. Auditors may append findings with agentq but must not edit production source code.

Run these auditors:

1. Completeness auditor
   - Use agents/completeness-auditor.md
   - Area: completeness

2. Correctness auditor
   - Use agents/correctness-auditor.md
   - Area: correctness

3. Perf allocation auditor
   - Use agents/perf-alloc-auditor.md
   - Area: perf_alloc

4. Perf algorithm auditor
   - Use agents/perf-algorithm-auditor.md
   - Area: perf_algorithm

Each auditor should:
- inspect its area
- deduplicate against existing findings
- append only high-quality actionable findings
- run agentq render for its area
- run agentq doctor
- report how many findings it added and the top 5 highest-priority findings

Wait for all auditors and summarize.
```
```
