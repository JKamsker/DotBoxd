# Contributing to DotBoxD

Thanks for your interest in DotBoxD. This guide covers building, testing, the CI gates your change
must pass, and commit conventions.

## Prerequisites

- **.NET SDK 10.0** (the repo pins `10.0.204` with `latestFeature` roll-forward via `global.json`).
- The build targets multiple runtimes — CI installs the **.NET 8, 9, and 10** SDKs. Projects target
  `net10.0` (Kernels/Pushdown) and `netstandard2.1` (Services/channels, for Unity/IL2CPP).
- PowerShell 7+ is needed to run the gate scripts under `eng/scripts/` locally.

## Build & test

The solution file is `DotBoxD.slnx`.

```bash
dotnet restore DotBoxD.slnx
dotnet build   DotBoxD.slnx -c Release --no-restore
dotnet test    DotBoxD.slnx -c Release --no-build
```

Run the maintained GameServer example:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

> Warnings are treated as errors in CI (`TreatWarningsAsErrors` is on when `GITHUB_ACTIONS=true`).
> To reproduce CI strictness locally, set the environment variable before building:
> `GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release`.

### Analyzers and formatting

The build runs the .NET analyzers (`AnalysisLevel` 10) plus **Roslynator** and **Meziantou**,
with every rule severity configured centrally in [`.editorconfig`](https://github.com/JKamsker/DotBoxD/blob/main/.editorconfig). Because warnings
are errors in CI, a new analyzer finding fails the build. The `.editorconfig` documents, per rule,
which rules are enforced and which are deliberately disabled (with the reason). Format your changes
with `dotnet format` before pushing — CI verifies formatting with
`dotnet format whitespace --verify-no-changes`.

## CI gates

Every pull request runs the `ci` workflow (`.github/workflows/ci.yml`):

**Build & test** — restore, build, and `dotnet test` on `ubuntu-latest` and `windows-latest` with
the .NET 8/9/10 SDKs.

**Security & quality gates** (`eng/scripts/`):

| Gate | Script | Checks |
|------|--------|--------|
| Rebrand completeness | `check-rebrand-complete.ps1` | No legacy `ShaRPC`/`SafeIR`/`SGP#` tokens in active source |
| Formatting | `dotnet format whitespace --verify-no-changes` | Code matches `.editorconfig` |
| C# file-length / layout | `check-csharp-file-lines.ps1` | File and folder size limits + a ratcheting soft-limit budget (see `AGENTS.md`) |
| Spec manifest integrity | `check-spec-manifest.ps1` | The sandbox spec manifest is consistent |
| Public API baseline | `check-api-compat-baseline.ps1` | Public package API matches `docs/api-baselines/*.txt` |
| Security-boundary tests | `run-required-tests.ps1` | A required allowlist of sandbox/security tests passes |
| Coverage threshold | `check-coverage.ps1` | Line coverage over the `DotBoxD.*` assemblies stays above the floor in `.config/code-enforcer/coverage.json` |
| Docs & examples smoke | `check-docs-smoke.ps1` | Doc commands point at real paths; the GameServer sample runs on Windows |

The `tests/DotBoxD.Architecture.Tests` project additionally enforces layer dependencies, naming
conventions, and that the analyzer configuration is not silently weakened; it runs as part of the
test matrix.

**Package validation** — after build/test and gates pass, CI packs every product package, runs
`check-package-metadata.ps1`, runs the package consumer smoke test, and uploads the package artifact.
Pushes to `main` in `JKamsker/DotBoxD` then publish `0.1.0-ci.<run>` prerelease packages to NuGet.org.

Additional workflows: **CodeQL** (`codeql.yml`, C# static analysis on push/PR + weekly) and
**benchmarks** (`benchmarks.yml`, scheduled and on PRs labeled `benchmark`). The **release**
workflow reuses the full `ci` workflow before packing, attesting provenance, and publishing.

If you change a public API on purpose, update the baseline with
`./eng/scripts/check-api-compat-baseline.ps1 -Update` and review the diff.

## Coding standards

See [`AGENTS.md`](https://github.com/JKamsker/DotBoxD/blob/main/AGENTS.md) for repository expectations and the C# size guard (files under ~300
lines where practical; folders containing a `.csproj` hold at most 5 tracked C# files unless
justified). Prefer many small, focused files over a few large ones, and add or update tests for any
behavior change.

## Commit conventions

Use [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>: <description>

<optional body>
```

Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`, `perf`, `ci`, `build`. Keep commits small
and reviewable; prefer one logical change per commit.

## Reporting security issues

Do **not** open a public issue for security vulnerabilities. Follow the private disclosure process in
[`SECURITY.md`](SECURITY.md).

## Code of Conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md). By participating you agree to
abide by it.
