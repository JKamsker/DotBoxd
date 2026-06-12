---
id: COR-0020
area: correctness
status: verified
priority: high
title: File write grants default to create-and-overwrite authority
dedup_key: security/file-write/grant-defaults/create-overwrite
created_at: 2026-06-12T22:13:41.4529604+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T22:22:25.3460692+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:15:46.4572342+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T22:19:04.2030868+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T22:22:25.3460692+00:00
verified_commit: 
duplicate_of: 
---

# COR-0020: File write grants default to create-and-overwrite authority

# File write grants default to create-and-overwrite authority

## Summary

`file.write` grants default to both creating missing files and overwriting existing files. A host that grants write access with the simplest API call, or constructs a direct `CapabilityGrant` without both optional booleans, gives untrusted IR the broadest write authority inside the granted root instead of requiring explicit opt-in for destructive behavior.

## Evidence

- `src/SafeIR.Core/Policy.cs` defines `SandboxPolicyBuilder.GrantFileWrite(string root, long maxBytesPerRun, bool allowCreate = true, bool allowOverwrite = true)`, so `.GrantFileWrite(root, limit)` permits both creating new files and overwriting existing files.
- The same builder serializes both flags only because the defaults are true; callers using direct policy construction can omit them entirely.
- `src/SafeIR.Runtime/Bindings/SafeFileWritePublisher.cs` reads those parameters with `ReadBool(grant, "allowCreate", fallback: true)` and `ReadBool(grant, "allowOverwrite", fallback: true)`, so missing policy flags also expand to create-and-overwrite at runtime.
- `src/SafeIR.Validation/PolicyGrantValidator.cs` validates `allowCreate` and `allowOverwrite` only when present; it does not require hosts to state those write modes explicitly for `file.write` grants.
- Existing tests exercise the permissive default: `tests/SafeIR.Tests/Misc07/SafeFileSystemTests.cs` calls `.GrantFileWrite(temp.Path, 1024)` and successfully creates `out/result.txt`.

## Impact

SafeIR's file capability model relies on explicit host whitelisting. With the current defaults, a host that intends to allow a plugin to update a known existing output file can accidentally allow creation of arbitrary new files under the root. Conversely, a host that intends append-or-create style output can accidentally allow overwriting existing configuration, cache, or checkpoint files under the same root. This is especially risky when the grant root is a broad tenant workspace or application data directory.

## Security test idea

Add a policy-boundary test that prepares a module requiring `file.write` with `SandboxPolicyBuilder.Create().GrantFileWrite(root, 1024)` and asserts the default grant denies at least overwriting an existing file unless `allowOverwrite: true` is passed explicitly. Add a direct-policy test where `new CapabilityGrant("file.write", new Dictionary<string,string> { ["root"] = root, ["maxBytesPerRun"] = "1024" })` is rejected at prepare time or treated as no-create/no-overwrite.

## Suggested fix direction

Make write authority fail closed. Either require `allowCreate` and `allowOverwrite` to be present for every `file.write` grant, or change both builder/runtime fallbacks to `false` and force callers to opt in explicitly. If compatibility is needed, add a clearly named helper such as `GrantFileWriteCreateAndOverwrite` for demos, but keep the basic grant API non-destructive by default.
