---
id: API-0020
area: api_coherence
status: open
priority: medium
title: File write grant docs omit create and overwrite policy controls
dedup_key: api:file-write-grant-create-overwrite-docs
created_at: 2026-06-12T23:24:21.1464714+00:00
created_by: completeness-api-producer
created_commit: 
updated_at: 2026-06-12T23:24:21.1464714+00:00
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

# API-0020: File write grant docs omit create and overwrite policy controls

## Claim

The public API documentation still shows `SandboxPolicyBuilder.GrantFileWrite(string root, long maxBytesPerRun)` as the file-write grant surface, but the shipped builder also exposes `allowCreate` and `allowOverwrite` flags that control whether writes can create missing targets or replace existing files. Those flags are part of the safe host contract, not an implementation detail.

## Why this matters

A host following the package-facing API page can grant `file.write` and still get denied writes because the documented two-argument call defaults both modes to `false`. Conversely, operators reviewing direct `file.write` grants need to know that create/overwrite are explicit policy decisions. Leaving those controls out of the public API docs makes file-write setup look incomplete and pushes users toward source/tests to discover required options.

## Evidence

- `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md` documents only `public SandboxPolicyBuilder GrantFileWrite(string root, long maxBytesPerRun);`.
- `src/DotBoxd.Kernels/Policy.cs` implements `GrantFileWrite(string root, long maxBytesPerRun, bool allowCreate = false, bool allowOverwrite = false)` and serializes both flags into the `file.write` grant.
- `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileWritePublisher.cs` reads `allowCreate` and `allowOverwrite` with `false` fallbacks and denies missing-target creation or existing-target overwrite unless the matching flag is enabled.
- `tests/DotBoxd.Kernels.Tests/Misc07/SafeFileSystemTests.cs` has coverage for successful create with `allowCreate: true`, overwrite denial, and the default builder grant denying both create and overwrite.

## Suggested test or benchmark

Add a docs/API smoke check that fails if the public API page omits policy-shaping parameters for public grant helpers. A focused assertion can require `GrantFileWrite` documentation to mention `allowCreate`, `allowOverwrite`, their defaults, and one create-vs-overwrite example.

## Suggested fix direction

Update `docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md` to show the full `GrantFileWrite` signature and explain the safe defaults. Add a short README or public API snippet showing a create-only grant and an overwrite-enabled grant so package consumers do not need to infer the behavior from tests.

## Scope boundaries

Do not loosen file-write policy defaults or runtime enforcement. This finding is about documenting and validating the user-facing API contract for the existing create/overwrite controls.

## Deduplication key

`api:file-write-grant-create-overwrite-docs`

## Verification checklist

- [ ] Public API docs show `allowCreate` and `allowOverwrite` with defaults.
- [ ] User-facing docs include at least one file-write grant example that chooses create/overwrite policy explicitly.
- [ ] Docs/API smoke catches future drift for file-write grant parameters.
- [ ] Existing file-write policy tests still pass.
