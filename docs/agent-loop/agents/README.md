# Agent Descriptions

This folder defines stable agent roles for the Codex workflow.

Use these as prompts or as source material for Codex custom agents/subagents.

## Roles

| Agent | Purpose | Source writes |
|---|---|---:|
| `coordinator` | Maintains plan/queues, assigns work | Rare |
| `completeness-auditor` | Finds missing features/behavior | No |
| `correctness-auditor` | Finds bugs/edge cases | No |
| `perf-alloc-auditor` | Finds allocation issues | No |
| `perf-algorithm-auditor` | Finds algorithmic inefficiencies | No |
| `api-coherence-auditor` | Finds API design inconsistencies | No |
| `test-coverage-auditor` | Finds missing/weak tests | No |
| `fixer` | Fixes one claimed finding | Yes |
| `verifier` | Confirms or reopens fixes | Usually no |
| `dedup-curator` | Deduplicates and cleans queues | No source writes |

## Global rule

Auditors append findings. Fixers fix. Verifiers verify. Do not merge roles unless explicitly requested.
