---
id: COR-0050
area: correctness
status: fixed_pending_verification
priority: high
title: Verifier switch operand decoding can exhaust memory before opcode rejection
dedup_key: security/verifier/il-reader/forbidden-switch-operand-unbounded-allocation
created_at: 2026-06-12T23:27:18.5928498+00:00
created_by: security-producer
created_commit: 
updated_at: 2026-06-12T23:45:13.3563319+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:43:46.4685491+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:45:13.3563319+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0050: Verifier switch operand decoding can exhaust memory before opcode rejection

## Claim

The generated-assembly verifier decodes `switch` operands and allocates an `int[count]` table before the opcode allowlist rejects `switch`. A malformed generated assembly or persistent-cache DLL can therefore force unbounded verifier allocation instead of producing a bounded diagnostic.

## Evidence

- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.Methods.cs:87` through `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.Methods.cs:90` reads method IL into `GeneratedInstruction` records before opcode verification and shape checks run.
- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedIlReader.cs:87` through `src/DotBoxd.Kernels.Verifier/Generated/GeneratedIlReader.cs:102` handles `ILOpCode.Switch` by reading the table count, allocating `new int[count]`, and then reading each target delta. There is no bound based on remaining IL bytes or a verifier maximum before the allocation.
- `src/DotBoxd.Kernels.Verifier/OpCodeVerifier.cs:8` through `src/DotBoxd.Kernels.Verifier/OpCodeVerifier.cs:24` does not include `ILOpCode.Switch` in the allowed opcode set.
- `src/DotBoxd.Kernels.Verifier/OpCodeVerifier.cs:48` through `src/DotBoxd.Kernels.Verifier/OpCodeVerifier.cs:54` only emits the forbidden-opcode diagnostic after instruction decoding has already completed.
- `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:105` through `src/DotBoxd.Kernels.Compiler/PersistentCompiledArtifactCache.cs:113` runs the verifier on cached `module.dll` bytes during cache reads, so a hostile cache entry can reach this path before being quarantined.

## Risk

The verifier is a trust boundary for generated artifacts, cache entries, and direct verifier consumers. A small malformed method body can encode a huge `switch` count and make verification allocate a massive array before the verifier has a chance to reject the forbidden opcode. That can crash or stall a host, CI verifier job, or cache-read path with memory pressure rather than returning a safe `VerificationResult` diagnostic.

This is distinct from verifier provenance findings such as `COR-0032` and `COR-0048`: this finding does not require accepting an unsafe artifact. The failure occurs while attempting to reject malformed IL.

## Suggested test

Add a verifier hardening test that builds or patches a generated assembly method body containing a `switch` opcode with a very large count and insufficient remaining operand bytes. The fixed verifier should return a bounded diagnostic such as `V-IL-FORMAT` or `V-OPCODE` without allocating proportional to the claimed count.

## Expected behavior

Verifier parsing should be bounded by the actual method body length and by explicit verifier limits. Unsupported variable-length opcodes should be rejected before allocating operand tables.

## Suggested fix direction

Reject unsupported opcodes before decoding variable-length operands, or add a bounded preflight in `GeneratedIlReader` that checks `switch` count against remaining bytes and a small maximum before allocating. Prefer not to materialize switch targets at all unless `switch` becomes an allowed opcode.

## Deduplication key

security/verifier/il-reader/forbidden-switch-operand-unbounded-allocation
