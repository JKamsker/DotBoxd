---
id: PAL-0029
area: perf_alloc
status: open
priority: medium
title: Policy grant setup reflects over parameters per grant
dedup_key: alloc/policy-builder/generic-grant/parameter-reflection-per-grant
created_at: 2026-06-12T22:33:59.2735911+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:33:59.2735911+00:00
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

# PAL-0029: Policy grant setup reflects over parameters per grant

## Claim

Generic policy grant setup reflects over parameter object metadata for every `SandboxPolicyBuilder.Grant(string, object)` call. Repeated construction of policies with the same anonymous/options type pays `GetProperties`, indexer filtering, reflection `GetValue`, and dictionary/read-only wrapper allocation for each grant instead of caching per parameter type.

## Evidence

- `src/DotBoxd.Kernels/Policy.cs:77` exposes the generic `Grant(string capabilityId, object parameters)` overload, and `src/DotBoxd.Kernels/Policy.cs:87` calls `ParameterReader.Read(parameters)` for every generic grant.
- `src/DotBoxd.Kernels/Policy.cs:294` calls `parameters.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)` on each read, so property metadata is rediscovered per grant even when the parameter type is reused.
- `src/DotBoxd.Kernels/Policy.cs:295` through `src/DotBoxd.Kernels/Policy.cs:302` filters public getters and indexers on every call.
- `src/DotBoxd.Kernels/Policy.cs:306` invokes `property.GetValue(parameters)` for every parameter property through reflection, then converts each value to an invariant string.
- `src/DotBoxd.Kernels/Policy.cs:293` through `src/DotBoxd.Kernels/Policy.cs:309` also creates a new `Dictionary<string,string>` and `ReadOnlyDictionary<string,string>` wrapper per grant. The dictionary copy is required for immutability, but the reflected metadata discovery and reflection invoke path are not cached.
- Dedicated grant helpers such as `GrantFileRead`, `GrantFileWrite`, `GrantTimeNow`, `GrantRandom`, and `GrantLogging` avoid this generic reflection path, so the issue is specifically custom/addon capability policy setup through the extensibility overload.
- This is distinct from `PAL-0020`, which covers policy hash recomputation after a policy exists, and from `PAL-0008`/`PAL-0022`, which cover runtime grant reparsing in HTTP/file bindings. This finding is setup-time reflection and allocation while constructing policies.

## Impact

Multi-tenant hosts, plugin servers, and tests/tooling can build many policies with the same custom capability parameter shapes. The generic grant API turns that setup into repeated reflection metadata enumeration and reflection calls proportional to grant count and property count. That makes policy construction slower and noisier in allocation profiles before validation, preparation, or execution begins.

## Better target

Cache readable parameter metadata per runtime type, ideally as compiled `Func<object, string?>` accessors with stable property names and invariant conversion behavior. Keep the existing dictionary snapshot for immutable grants, but avoid repeated `GetProperties`/filtering and reflection `GetValue` when the same parameter type is used many times. Preserve the direct dictionary fast path for callers that already provide `IReadOnlyDictionary<string,string>`.

## Benchmark/allocation test idea

Add a policy setup benchmark that creates 1, 10, 100, and 1,000 policies using `Grant("custom.capability", new { Root = ..., MaxBytes = ..., Enabled = ... })` and a reused custom parameter class. Measure elapsed time and allocated bytes before and after caching parameter readers, and compare against the existing dictionary fast path.
