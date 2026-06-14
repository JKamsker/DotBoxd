---
id: COR-0051
area: correctness
status: fixed_pending_verification
priority: high
title: Release file-length gate executes an unlocked restored dotnet tool
dedup_key: security/release-scripts/dotnet-tool-restore/unlocked-codeenforcer-execution
created_at: 2026-06-12T23:27:19.9909696+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T23:41:00.0827034+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:39:02.6072034+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:41:00.0827034+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0051: Release file-length gate executes an unlocked restored dotnet tool

## Claim

The package-producing CI path executes a restored .NET local tool from NuGet without a lock file, content hash, trusted-signer policy, or isolated non-release job boundary. The file-length gate can therefore introduce external tool code execution into release package production even though project package restore itself uses locked mode.

## Evidence

- `.github/workflows/ci.yml:69` through `.github/workflows/ci.yml:71` runs `./scripts/check-csharp-file-lines.ps1` in the same `build-test-pack` job that later packs and uploads release artifacts.
- `.github/workflows/ci.yml:94` through `.github/workflows/ci.yml:97` runs `dotnet pack DotBoxd.Kernels.slnx --configuration Release --no-build --output artifacts/packages` after that tool gate.
- `.github/workflows/ci.yml:117` through `.github/workflows/ci.yml:124` uploads the resulting `.nupkg` files as package artifacts.
- `scripts/check-csharp-file-lines.ps1:11` through `scripts/check-csharp-file-lines.ps1:13` names the external `CodeEnforcer` tool and version.
- `scripts/check-csharp-file-lines.ps1:43` through `scripts/check-csharp-file-lines.ps1:56` inspects local tools and may install the tool during CI.
- `scripts/check-csharp-file-lines.ps1:56` through `scripts/check-csharp-file-lines.ps1:61` runs `dotnet tool restore` and then executes `dotnet tool run code-enforcer`.
- `.config/dotnet-tools.json:5` through `.config/dotnet-tools.json:10` pins only the tool ID/version/roll-forward setting. There is no committed tool lock file or content hash check analogous to `.github/workflows/ci.yml:28` using `dotnet restore DotBoxd.Kernels.slnx --locked-mode` for project dependencies.

## Risk

Release package jobs should not execute mutable external code unless that code is covered by the release trust model. A compromised feed, prerelease tool package, local tool resolution source, or restore configuration could run arbitrary code in the same workspace before `dotnet pack --no-build` packages already-built outputs. That code could modify build outputs, package inputs, or validation scripts while the existing metadata gate still reports expected package IDs, versions, repository commit, and selected entries.

This is distinct from `COR-0040` mutable GitHub action tags, `COR-0046` ambient `GITHUB_TOKEN` permissions, `COR-0035` unexpected NuGet payloads, and `COR-0044` unsigned/unattested packages. This finding is specifically about a release script restoring and executing a NuGet tool without locked tool provenance.

## Suggested test

Add a release workflow/script lint that fails if a package-producing job runs `dotnet tool install`, `dotnet tool restore`, or `dotnet tool run` unless the tool is covered by an approved lock/provenance policy. Include a negative fixture matching `scripts/check-csharp-file-lines.ps1` and a positive fixture for a repo-built or locked tool.

## Expected behavior

Code quality gates that execute external tools should either run in an isolated pre-package job whose outputs cannot affect release artifacts, or use a committed and verified tool provenance mechanism equivalent to the locked project restore policy.

## Suggested fix direction

Prefer building CodeEnforcer from reviewed source in the repository or vendoring the gate as source. If it must remain a NuGet local tool, add a locked restore/provenance check for dotnet tools, restrict restore sources through reviewed configuration, verify signatures or hashes before execution, and keep package production in a separate job that consumes only reviewed build outputs.

## Deduplication key

security/release-scripts/dotnet-tool-restore/unlocked-codeenforcer-execution
