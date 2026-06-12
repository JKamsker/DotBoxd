---
id: PAL-0008
area: perf_alloc
status: verified
priority: low
title: HTTP grants are reparsed into sets per request
dedup_key: alloc/http-grants/runtime/csv-set-reparse-per-request
created_at: 2026-06-12T21:03:31.7748479+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:06:31.8313470+00:00
claimed_by: fixer
claimed_at: 2026-06-12T22:00:31.1419359+00:00
claim_branch: workflow-work
fixed_by: fixer
fixed_at: 2026-06-12T22:03:48.4483724+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T22:06:31.8313470+00:00
verified_commit: 
duplicate_of: 
---

# PAL-0008: HTTP grants are reparsed into sets per request

## Claim

HTTP capability grant parameters are reparsed into new collections on every `net.http.get` call even though grants are stable for an execution plan.

## Evidence

- `src/SafeIR.Runtime/Bindings/SafeHttpClient.cs:91` resolves the request for every HTTP binding call and then checks scheme, host, byte limits, and timeout from the grant.
- `src/SafeIR.Runtime/Bindings/SafeHttpClient.cs:180` calls `SafeHttpGrantReader.ReadSet(grant, "allowedSchemes", ["https"])` for each request.
- `src/SafeIR.Runtime/Bindings/SafeHttpClient.cs:189` calls `SafeHttpGrantReader.ReadSet(grant, "allowedHosts", [])` for each request.
- `src/SafeIR.Transport.Http/Internal/SafeHttpGrantReader.cs:9` builds fallback text with `string.Join` when needed, then `src/SafeIR.Transport.Http/Internal/SafeHttpGrantReader.cs:10` splits the CSV string and `src/SafeIR.Transport.Http/Internal/SafeHttpGrantReader.cs:11` materializes a new `HashSet<string>`.
- `src/SafeIR.Runtime/Bindings/SafeHttpClient.cs:214`, `src/SafeIR.Runtime/Bindings/SafeHttpClient.cs:224`, and `src/SafeIR.Runtime/Bindings/SafeHttpClient.cs:229` also reparse boolean grant parameters for DNS/IP checks during each request.
- Existing network tests cover policy behavior, but there is no allocation benchmark for repeated HTTP binding calls under the same grant.

## Impact

Modules that perform multiple allowed HTTP reads under the same policy repeatedly allocate split arrays and hash sets for unchanged grant data. This adds avoidable per-request allocation on a network binding path that already needs strict accounting and predictable overhead.

## Better target

Parse and validate HTTP grant parameters once when building or validating the policy/execution plan, then store typed grant data for runtime use. At minimum, cache parsed sets and scalar values per `CapabilityGrant` instance for the duration of an execution plan.

## Benchmark/allocation test idea

Add an allocation benchmark using an in-memory HTTP invoker that performs 1, 10, and 1,000 `net.http.get` calls under the same grant. Measure allocations from request validation separately from response body allocation, and assert repeated calls do not allocate new allowed-host/scheme sets.
