---
id: API-0011
area: api_coherence
status: open
priority: medium
title: Stable release gate allows a prerelease IPC transport package
dedup_key: api-stable-release-allows-prerelease-ipc-package
created_at: 2026-06-12T22:20:26.1230485+00:00
created_by: codex-api-producer
created_commit: 
updated_at: 2026-06-12T22:20:26.1230485+00:00
claimed_by: 
claimed_at: 
claim_branch: 
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# API-0011: Stable release gate allows a prerelease IPC transport package

## Claim
Stable release validation explicitly allows `SafeIR.Transport.Ipc.ShaRpc` to remain prerelease and to ship prerelease `ShaRPC` dependencies, so a stable SafeIR tag can publish an incomplete public package surface without a separate release-channel boundary.

## Evidence
- `src/SafeIR.Transport.Ipc.ShaRpc/SafeIR.Transport.Ipc.ShaRpc.csproj` references `ShaRPC`, `ShaRPC.Serializers.MessagePack`, and `ShaRPC.Transports.NamedPipes` at `1.0.0-ci.30`, and sets `<VersionSuffix>sharpc-ci.30</VersionSuffix>`.
- `.github/workflows/ci.yml` passes `AllowedPrereleasePackageIds = @("SafeIR.Transport.Ipc.ShaRpc")` in the stable release metadata gate.
- `scripts/check-package-metadata.ps1` has an `allowedPrereleaseDependenciesByPackage` entry for `SafeIR.Transport.Ipc.ShaRpc`, and `IsAllowedPrereleaseDependency` accepts dependency versions that start with `1.0.0-ci.`.
- Existing package metadata findings cover generic metadata text and consumer smoke coverage, but they do not require the IPC transport to be excluded from stable releases or promoted only after its dependencies are stable.

## Impact
The stable package set can look release-ready while one public transport package remains tied to CI builds of an external dependency stack. Consumers installing the SafeIR package family from a stable release may unknowingly depend on a preview IPC transport whose compatibility and support level differ from the rest of the release.

## Better target
Make the release channel explicit: either mark `SafeIR.Transport.Ipc.ShaRpc` non-packable until ShaRPC dependencies are stable, publish it only from a preview channel with clear versioning, or require stable ShaRPC dependency versions before stable SafeIR tags can include this package.

## Acceptance test idea
For release branches and tags, `check-package-metadata.ps1` should fail if any public package version or dependency version is prerelease unless the release is explicitly a prerelease channel and the package is documented as excluded from stable support.

## Deduplication key
api-stable-release-allows-prerelease-ipc-package
