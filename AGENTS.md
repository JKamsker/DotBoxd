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
- `CodeEnforcer` fails tracked C# files over 350 lines unless they are listed in `.config/code-enforcer/justifications.json`.
- Files over 500 lines require both an exclusion and a non-empty justification.
- The count of files over the 300-line soft cap is a ratcheting budget (`maxSoftLimitViolations` in `.config/code-enforcer/code-enforcer.json`): it may shrink but never grow. Split a file or, only when intentional, raise the budget.
- Folders over 15 tracked C# files must be listed in `.config/code-enforcer/justifications.json`; prefer focused subdirectories and namespaces for new code.
- Folders containing a `.csproj` may contain at most 5 tracked C# files unless listed in `.config/code-enforcer/justifications.json`.
- Split large code through composition and focused helper types, not partial classes used only to hide line count.

## Validation

- Build: `dotnet build DotBoxD.slnx -c Release`. The build runs the .NET analyzers plus Roslynator
  and Meziantou; rule severities live in `.editorconfig`. CI treats warnings as errors, so reproduce
  it locally with `GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release`.
- Format: `dotnet format` before committing (CI verifies with `dotnet format whitespace --verify-no-changes`).
- Test: `dotnet test DotBoxD.slnx -c Release` (run per-project on CI; see `.github/workflows/ci.yml`).
  `tests/DotBoxD.Architecture.Tests` guards layer dependencies, conventions, and the analyzer config.
- Quality gates live in `eng/scripts/` (security-boundary suite, API baselines, file-length + soft-limit
  budget, coverage threshold, spec manifest, rebrand-completeness, docs smoke) and run in CI.
