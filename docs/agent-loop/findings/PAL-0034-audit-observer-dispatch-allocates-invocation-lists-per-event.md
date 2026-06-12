---
id: PAL-0034
area: perf_alloc
status: open
priority: medium
title: Audit observer dispatch allocates invocation lists per event
dedup_key: alloc/audit-observer/dispatch/invocation-list-per-event
created_at: 2026-06-12T23:03:00.0468570+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T23:03:00.0468570+00:00
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

# PAL-0034: Audit observer dispatch allocates invocation lists per event

## Claim

Forwarding audit events to host observers allocates a multicast invocation-list array for every audit event. Even when observer registration is stable for the lifetime of the host, `SandboxHost` rebuilds the delegate array per event during result publication.

## Evidence

- `src/SafeIR.Hosting/Execution/SandboxHostBuilder.cs:82` through `src/SafeIR.Hosting/Execution/SandboxHostBuilder.cs:85` registers audit observers by appending to the `_auditObserver` multicast delegate.
- `src/SafeIR.Hosting/Execution/SandboxHost.cs:129` through `src/SafeIR.Hosting/Execution/SandboxHost.cs:139` publishes every event in `result.AuditEvents` after each execution result is produced.
- `src/SafeIR.Hosting/Execution/SandboxHost.cs:144` through `src/SafeIR.Hosting/Execution/SandboxHost.cs:150` calls `_auditObserver!.GetInvocationList()` inside `PublishToAuditObservers`, so the invocation-list array is allocated once per audit event, then each delegate is cast and invoked.
- Audit event counts grow with per-call/per-resource auditing and with fallback or worker failure paths; the observer set usually changes only during host construction.
- Existing `PAL-0021` covers copies while constructing `SandboxExecutionResult.AuditEvents`. This finding is the separate post-result observer forwarding allocation. Existing `PAL-0027` covers sanitizer cost and `PAL-0033` covers plugin execution observation retention, not host audit observer dispatch.

## Impact

A host with one or more audit observers pays an avoidable allocation for every forwarded `SandboxAuditEvent`. Under per-call or per-resource audit levels, high-frequency sandbox runs can produce many events, making observer forwarding allocate O(event-count * observer-count) delegate references even though the observer list is stable. This adds allocation pressure to the execution completion path and downstream telemetry integrations.

## Better target

Snapshot observers once when building the host, or maintain a copy-on-write observer array as observers are registered. Publish should use a cached array, and optionally a direct single-observer fast path, while preserving the existing exception isolation semantics for each observer.

## Benchmark/allocation test idea

Add an allocation benchmark that runs a module producing 1, 100, and 10,000 audit events with 1, 2, and 5 registered audit observers. Assert result publication does not allocate a fresh invocation-list array per event after observer registration has stabilized.
