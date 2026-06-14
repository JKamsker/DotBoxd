---
id: CMP-0004
area: completeness
status: verified
priority: high
title: CodeEnforcer gate fails on project-folder C# file-count violations
dedup_key: ci/codeenforcer/project-folder-file-count-violations
created_at: 2026-06-12T20:39:36.2103936+00:00
created_by: ci-release-readiness-reviewer
created_commit: 
updated_at: 2026-06-12T20:56:51.2396331+00:00
claimed_by: implementer
claimed_at: 2026-06-12T20:54:36.5964311+00:00
claim_branch: workflow-work
fixed_by: implementer
fixed_at: 2026-06-12T20:55:00.8456152+00:00
fixed_commit: working-tree
verified_by: Codex verifier
verified_at: 2026-06-12T20:56:51.2396331+00:00
verified_commit: 
duplicate_of: 
---

# CMP-0004: CodeEnforcer gate fails on project-folder C# file-count violations

## Claim
The CodeEnforcer CI gate currently fails because five project folders contain more than the configured maximum of five C# files beside a `.csproj` and are not justified in `.config/code-enforcer/justifications.json`.

## Why this matters
`.github/workflows/ci.yml` runs `./scripts/check-csharp-file-lines.ps1` on every CI job. A failing CodeEnforcer gate blocks pull requests and releases independently of build/test correctness.

## Evidence
Local reproduction:

```powershell
.\scripts\check-csharp-file-lines.ps1
```

Failure:

```text
CE0004 src/DotBoxd.Kernels.Compiler: contains a .csproj and 6 C# files, exceeding the project-folder limit of 5.
CE0004 src/DotBoxd.Kernels.Interpreter: contains a .csproj and 6 C# files, exceeding the project-folder limit of 5.
CE0004 src/DotBoxd.Kernels.Serialization.Json: contains a .csproj and 6 C# files, exceeding the project-folder limit of 5.
CE0004 src/DotBoxd.Kernels.Validation: contains a .csproj and 6 C# files, exceeding the project-folder limit of 5.
CE0004 tools/AgentQueue/src/AgentQueue: contains a .csproj and 6 C# files, exceeding the project-folder limit of 5.
CodeEnforcer found 5 violation(s).
```

The committed justification config currently contains only a file-level justification for `src/DotBoxd.Kernels.Validation/FunctionAnalyzer.cs` and no folder/root-folder exclusions for these project folders.

## Suggested test or benchmark
Run the CI CodeEnforcer gate after the fix:

```powershell
.\scripts\check-csharp-file-lines.ps1
```

Also run it from a clean checkout or CI job to confirm the local tool restore path still works.

## Suggested fix direction
Prefer moving implementation files into focused subdirectories/namespaces so each `.csproj` folder stays under the project-root file-count limit. If a folder genuinely needs an exception, add a narrow justification to `.config/code-enforcer/justifications.json` instead of weakening the global limit.

## Scope boundaries
Do not silence CodeEnforcer globally. Do not use partial classes or broad exclusions solely to hide file count; follow the repository size-guard guidance.

## Deduplication key
`ci/codeenforcer/project-folder-file-count-violations`

## Verification checklist
- [ ] Reproduction or test exists where practical.
- [ ] Fix addresses root cause.
- [ ] Relevant tests pass.
- [ ] Perf/allocation evidence exists where practical.
- [ ] No unrelated behavior changed.
