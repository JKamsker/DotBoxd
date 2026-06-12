---
id: COR-0014
area: correctness
status: open
priority: medium
title: Public audit and error payloads expose mutable collection inputs
dedup_key: correctness/public-model/audit-error-mutable-collection-escape
created_at: 2026-06-12T22:06:03.1286042+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T22:06:03.1286042+00:00
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

# COR-0014: Public audit and error payloads expose mutable collection inputs

## Claim

Public error and audit result payloads keep caller-owned mutable collections even though they are exposed as `IReadOnlyList` / `IReadOnlyDictionary`. That lets diagnostics or audit evidence change after validation, publication, or result delivery.

## Evidence

`src/SafeIR.Core/Model/Diagnostics.cs:18` to `src/SafeIR.Core/Model/Diagnostics.cs:23` stores the constructor argument directly: `SandboxValidationException.Diagnostics { get; } = diagnostics;`. A caller that constructs the public exception with a `List<SandboxDiagnostic>` can mutate the list after construction and change the exception's reported diagnostics.

`src/SafeIR.Core/Bindings/Audit.cs:19` to `src/SafeIR.Core/Bindings/Audit.cs:32` defines `SandboxAuditEvent.Fields` as an `IReadOnlyDictionary<string, string>?` positional record property without copying it. `InMemoryAuditSink.Events` returns the backing list as `IReadOnlyList<SandboxAuditEvent>` at `src/SafeIR.Core/Bindings/Audit.cs:49` to `src/SafeIR.Core/Bindings/Audit.cs:61`, and `Write` only adds `auditEvent with { SequenceNumber = sequence }`; it does not snapshot `Fields`.

Execution results expose those mutable audit objects directly. The public `SandboxExecutionResult` type has `IReadOnlyList<SandboxAuditEvent> AuditEvents` at `src/SafeIR.Core/ExecutionPlan.cs:111` to `src/SafeIR.Core/ExecutionPlan.cs:118`. Interpreted and compiled execution assign `AuditEvents = audit.Events` at `src/SafeIR.Interpreter/SandboxInterpreter.cs:64` to `src/SafeIR.Interpreter/SandboxInterpreter.cs:70` and `src/SafeIR.Hosting/Execution/CompiledExecutionRunner.cs:70` to `src/SafeIR.Hosting/Execution/CompiledExecutionRunner.cs:76`. Worker isolation resequences events, but `SandboxAuditEventSequence.ToSequencedArray` at `src/SafeIR.Hosting/SandboxHost.Results.cs:328` to `src/SafeIR.Hosting/SandboxHost.Results.cs:338` still calls `sink.Write(auditEvent)`, so the copied event keeps the same mutable `Fields` dictionary reference.

Existing immutability coverage in `tests/SafeIR.Tests/Misc06/PublicModelImmutabilityTests.cs:10` to `tests/SafeIR.Tests/Misc06/PublicModelImmutabilityTests.cs:96` covers modules, statements, plugin manifests, and sandbox values, but it does not cover `SandboxValidationException.Diagnostics`, `SandboxAuditEvent.Fields`, `InMemoryAuditSink.Events`, or `SandboxExecutionResult.AuditEvents`.

A minimal reproducer for the audit half is:

```csharp
var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["resourceKind"] = "network" };
var sink = new InMemoryAuditSink();
sink.Write(new SandboxAuditEvent(SandboxRunId.New(), "BindingCall", DateTimeOffset.UtcNow, true, Fields: fields));
fields["resourceKind"] = "mutated";
Assert.Equal("network", sink.Events[0].Fields!["resourceKind"]); // currently fails
```

The same issue applies to a custom or in-process worker that returns a valid `SandboxExecutionResult` and retains references to audit field dictionaries: `SandboxWorkerExecutor.ValidateWorkerResult` validates the fields before returning, but the accepted result still exposes the same mutable field dictionaries after resequencing.

## Risk

Audit events and validation diagnostics are evidence objects. If their collection payloads can change after construction or after worker-result validation, callers and audit observers can observe different diagnostics or audit fields for the same sandbox result depending on timing and external mutation. This undermines public model immutability and can make audit/error consistency checks pass on one view of a result while later consumers see another.

## Suggested test

Extend `PublicModelImmutabilityTests` with tests that:

- construct `SandboxValidationException` from a mutable `List<SandboxDiagnostic>`, mutate the list, and assert `Diagnostics` is unchanged;
- write a `SandboxAuditEvent` with a mutable `Dictionary<string,string>` into `InMemoryAuditSink`, mutate the dictionary, and assert the stored event fields are unchanged;
- execute a simple module and assert `result.AuditEvents` is not backed by a mutable `List<SandboxAuditEvent>` exposed to callers, and audit event `Fields` are not mutable through a retained source dictionary;
- add a worker-result hardening case where a test worker returns an audit summary with a retained mutable field dictionary and mutates it after validation, then assert the host result keeps the validated fields.

## Expected behavior

Public diagnostics, audit events, and execution results should be immutable snapshots. Mutating a caller-owned list or dictionary after constructing an exception/event/result must not change what later consumers observe.

## Suggested fix direction

Snapshot collections at the public boundaries. For example, copy diagnostics in `SandboxValidationException`, copy `SandboxAuditEvent.Fields` into a read-only dictionary when events are written or constructed, and return immutable arrays/snapshots for `InMemoryAuditSink.Events` and `SandboxExecutionResult.AuditEvents`. Keep the copies shallow for scalar field values; the important boundary is preventing list/dictionary identity from escaping.

## Deduplication key

`correctness/public-model/audit-error-mutable-collection-escape`
