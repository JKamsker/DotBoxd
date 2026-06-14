---
id: PAL-0017
area: perf_alloc
status: open
priority: medium
title: Verifier decodes member signatures across repeated metadata passes
dedup_key: alloc/verifier/metadata/member-signature-decode-repeated-pass
created_at: 2026-06-12T22:11:12.8138335+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:11:12.8138335+00:00
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

# PAL-0017: Verifier decodes member signatures across repeated metadata passes

## Claim

Generated assembly verification decodes member signatures multiple times across metadata and IL passes, so methods with many runtime/helper calls allocate repeated signature strings and parameter type strings before the stack/meter verifiers run.

## Evidence

- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.cs:66` runs `VerifyMemberReferences` over the assembly metadata before method-body verification.
- `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.cs:121` iterates every `MemberReference`, and `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.cs:128` decodes each via `MetadataName.MemberSignature`.
- During method body reading, `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.Methods.cs:88` calls `GeneratedIlReader.ReadInstructions`, and `src/DotBoxd.Kernels.Verifier/Generated/GeneratedIlReader.cs:75` decodes the call operand with `MetadataName.MemberSignature` again.
- The same instruction list then goes through `OpCodeVerifier.VerifyBody` at `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.Methods.cs:89`; `src/DotBoxd.Kernels.Verifier/OpCodeVerifier.cs:92` decodes `MetadataName.MemberSignature` a third time from the saved operand handle.
- `src/DotBoxd.Kernels.Verifier/Generated/MetadataName.cs:65` uses `DecodeMethodSignature(...)`, and `src/DotBoxd.Kernels.Verifier/Generated/MetadataName.cs:88` formats a new signature string with `string.Join` for each decode.
- Existing verifier call benchmarks exercise runtime calls, but they do not isolate or assert metadata signature decode reuse across verifier passes.

## Impact

Compiled artifacts with thousands of calls to a small set of runtime helpers or binding dispatch stubs repeatedly reconstruct identical signature strings and parameter type names during verification. Verification runs on compile/cache-hit materialization paths, so this adds avoidable Gen0 pressure and CPU before execution can start.

## Better target

Decode each member/method signature once per metadata token and pass the decoded signature through `GeneratedInstruction`, `OpCodeVerifier`, and method-shape checks. A per-verification dictionary keyed by `EntityHandle` or metadata token would keep allowlist checks and stack/meter analysis on shared immutable signature metadata.

## Benchmark/allocation test idea

Extend the verifier benchmark with methods containing 100, 1,000, and 10,000 calls to the same few `CompiledRuntime` helpers plus a case with many distinct helpers. Measure `GeneratedAssemblyVerifier.VerifyAsync` allocations and add a regression assertion that identical call tokens are decoded once per verification pass, not once in each metadata/IL/opcode traversal.
