---
id: COR-0035
area: correctness
status: claimed
priority: high
title: Package metadata gate allows unexpected NuGet payloads
dedup_key: security/release-packaging/nuget/unexpected-package-payloads
created_at: 2026-06-12T22:31:19.1576455+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T23:22:20.4987227+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:22:20.4987227+00:00
claim_branch: workflow-work
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0035: Package metadata gate allows unexpected NuGet payloads

## Claim

The release package metadata gate allows product `.nupkg` files to contain unexpected NuGet payloads, including `build/`, `buildTransitive/`, `contentFiles/`, `tools/`, `native/`, or extra analyzer/runtime files, as long as the required DLL/readme/metadata entries are present.

## Evidence

`scripts/check-package-metadata.ps1` opens each `.nupkg` and validates metadata fields, package IDs, versions, repository commit, readme, license, and prerelease dependencies. For package contents, it only requires `analyzers/dotnet/cs/SafeIR.PluginAnalyzer.dll` and no `lib/` entries for `SafeIR.PluginAnalyzer`, or `lib/net10.0/$id.dll` for every other package. It does not reject unexpected package entries after those presence checks.

Relevant code:

- `scripts/check-package-metadata.ps1:86` through `scripts/check-package-metadata.ps1:104` define helpers that can assert required entries or reject a prefix, but the prefix rejection is only used for analyzer `lib/` contents.
- `scripts/check-package-metadata.ps1:221` through `scripts/check-package-metadata.ps1:226` require the analyzer DLL or library DLL, then continue to dependency checks without enumerating and denying extra package roots.
- `.github/workflows/ci.yml:94` through `.github/workflows/ci.yml:126` packs, runs this checker, and uploads `artifacts/packages/*.nupkg` as release artifacts.
- `Directory.Build.props:24` intentionally packs `README.md`, and `src/SafeIR.PluginAnalyzer/SafeIR.PluginAnalyzer.csproj:9` intentionally packs the analyzer DLL, so the release gate already has enough package-layout knowledge to enforce an allowlist per package kind.

A package could therefore include a `buildTransitive/*.targets` file, `tools/*`, `contentFiles/*`, `native/*`, an extra analyzer DLL in a runtime package, or another unexpected payload and still pass the current release gate if the normal required files remain present.

## Risk

NuGet package contents are a downstream trust boundary. Extra `build` or `buildTransitive` targets can execute MSBuild logic in consumers; extra analyzers can run during compilation; native/tools/content payloads can add files that are outside SafeIR's reviewed package surface. Because CI uploads the checked `.nupkg` artifacts, an accidental project file change or compromised packaging step could produce a package with executable or policy-relevant payloads while the release gate still reports success.

## Suggested test

Add a package metadata test fixture or scripted check that creates/copies a valid package, injects an unexpected entry such as `buildTransitive/SafeIR.targets` or `tools/payload.ps1`, and asserts `check-package-metadata.ps1` fails. Add positive cases for the expected runtime package layout and the analyzer package layout.

## Expected behavior

The release gate should fail closed on unknown NuGet package entries. Runtime packages should allow only the expected nuspec/signature metadata, README/license conventions, and `lib/net10.0/<id>.dll` plus explicitly approved symbol/source artifacts if added later. The analyzer package should allow only the expected analyzer path and approved metadata/readme entries.

## Suggested fix direction

Extend `check-package-metadata.ps1` with per-package entry allowlists or denylisted executable roots. At minimum, reject `build/`, `buildTransitive/`, `content/`, `contentFiles/`, `tools/`, `native/`, unexpected `analyzers/` entries, and unexpected `lib/` entries. Keep the existing required-entry checks and prerelease dependency policy; this should tighten package contents without weakening metadata validation.

## Deduplication key

`security/release-packaging/nuget/unexpected-package-payloads`
