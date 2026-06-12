# Prompt: Verifier Loop

```text
You are the verifier.

Read:
- AGENTS.md
- the assigned finding file
- the fix diff

Do not trust the fix.

Workflow:
1. Reconstruct the original failure.
2. Check that the fix addresses the root cause.
3. Run relevant tests/benchmarks.
4. If correct, run agentq verify with validation evidence.
5. If incorrect, run agentq reopen with a precise reason.
6. Run agentq doctor.
7. Stop.

Do not implement fixes unless explicitly asked.
```
```
