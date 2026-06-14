---
id: PAL-0024
area: perf_alloc
status: open
priority: medium
title: Binding audit fields allocate duplicate dictionaries
dedup_key: alloc/audit/binding-fields/double-dictionary-per-event
created_at: 2026-06-12T22:22:54.8196331+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:22:54.8196331+00:00
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

# PAL-0024: Binding audit fields allocate duplicate dictionaries

## Claim

Binding audit field creation allocates and copies a dictionary twice for every audit event that includes module and policy hashes.

## Evidence

- `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:188` through `src/DotBoxd.Kernels/Sandbox/SandboxContext.cs:200` routes binding audit field creation through `BindingAuditFields.Create(...)` with `ModuleHash` and `PolicyHash`.
- `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:5` through `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:18` implements that overload by first calling the simpler overload, then immediately calling `.ToDictionary(...)` to copy the returned fields before adding `moduleHash` and `policyHash`.
- The simpler overload at `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:21` through `src/DotBoxd.Kernels/Bindings/BindingAuditFields.cs:37` already creates a new `Dictionary<string, string>` and populates `resourceKind`, `durationMs`, and optional byte fields.
- File audit events call this path through `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileAudit.cs:36` through `src/DotBoxd.Kernels.Runtime/Bindings/SafeFileAudit.cs:40`.
- HTTP audit events call it through `src/DotBoxd.Hosting.Http/SafeHttpClient.cs:297` through `src/DotBoxd.Hosting.Http/SafeHttpClient.cs:308`.
- Log audit events call it through `src/DotBoxd.Kernels.Runtime/Bindings/SafeLogBindings.cs:36` through `src/DotBoxd.Kernels.Runtime/Bindings/SafeLogBindings.cs:46`.
- `SandboxAuditEvent` also snapshots `Fields` on construction at `src/DotBoxd.Kernels/Bindings/Audit.cs:34` through `src/DotBoxd.Kernels/Bindings/Audit.cs:39`, so the extra `.ToDictionary` copy happens before the public audit-event defensive copy.
- This is distinct from `PAL-0021`, which covers copying audit event lists into execution results. The issue here is per-event field dictionary construction doing an avoidable intermediate dictionary copy.

## Impact

Every audited binding call that emits standard binding fields allocates one dictionary in `BindingAuditFields.Create`, copies it into a second dictionary with LINQ, then has the audit event snapshot fields again. For high-volume audited bindings such as logs, file reads, HTTP requests, plugin messages, or required per-call audit bindings, this adds avoidable Gen0 pressure on the audit hot path.

## Better target

Build the final field dictionary once in the module/policy overload, with enough initial capacity for base fields, optional byte fields, `moduleHash`, and `policyHash`. Keep the `SandboxAuditEvent` boundary copy if public immutability requires it, but avoid copying a dictionary that was just created internally.

## Benchmark idea

Add an allocation benchmark for 10,000 binding audit events across log, file, and HTTP paths. Measure allocated bytes before and after replacing the `Create(...).ToDictionary(...)` pattern with a single dictionary construction.
