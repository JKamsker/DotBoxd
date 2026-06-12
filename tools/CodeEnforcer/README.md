# CodeEnforcer

`CodeEnforcer` is the repository-local C# structure gate.

Run it through the existing CI/hook script:

```powershell
./scripts/check-csharp-file-lines.ps1
```

Or directly:

```powershell
dotnet run --project tools/CodeEnforcer/src/CodeEnforcer -- --config tools/CodeEnforcer/code-enforcer.json
```

Rules:

- A tracked C# file over 350 lines must be listed in `code-enforcer.json`.
- A tracked C# file over 500 lines must be listed and have a non-empty justification.
- A folder with more than 15 tracked C# files must be listed in `code-enforcer.json`.

Exclusions are central on purpose so technical debt is visible in review.
