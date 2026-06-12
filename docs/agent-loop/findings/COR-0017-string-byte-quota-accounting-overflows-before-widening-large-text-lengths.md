---
id: COR-0017
area: correctness
status: verified
priority: medium
title: String byte quota accounting overflows before widening large text lengths
dedup_key: correctness/resource-meter/string-byte-count-overflow-before-widening
created_at: 2026-06-12T22:09:35.0577229+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T22:47:25.2023769+00:00
claimed_by: worker
claimed_at: 2026-06-12T22:20:15.7584512+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T22:38:25.6288446+00:00
fixed_commit: 
verified_by: verifier
verified_at: 2026-06-12T22:47:25.2023769+00:00
verified_commit: 
duplicate_of: 
---

# COR-0017: String byte quota accounting overflows before widening large text lengths

## Claim

String byte quota accounting multiplies `string.Length` by `sizeof(char)` as an `int` before assigning the result to `long`. A very large host-provided string can therefore wrap the byte count before quota checks, allowing `StringBytes`, allocation charging, and binding per-byte fuel to be undercounted or treated as zero/negative instead of failing closed.

## Evidence

`src/SafeIR.Core/Model/Resources.cs:104` to `src/SafeIR.Core/Model/Resources.cs:108` implements `ResourceMeter.ChargeString` as:

```csharp
var bytes = value.Length * sizeof(char);
ChargeStringShape(new ValueShape(0, 0, 0, 0, value.Length, bytes));
```

Because both operands are `int`, the multiplication overflows before the value is widened into `ValueShape.StringBytes` (`long`). `ChargeStringAllocation` in the same file correctly does `checked((long)charLength * sizeof(char))`, so the allocation path already shows the intended fail-closed behavior.

`src/SafeIR.Core/Sandbox/SandboxLiteralConstraints.cs:115` to `src/SafeIR.Core/Sandbox/SandboxLiteralConstraints.cs:116` has the same issue in `TextShape`:

```csharp
internal static ValueShape TextShape(string value)
    => new(0, 0, 0, 0, value.Length, value.Length * sizeof(char));
```

That shape is consumed by `SandboxValueShapeMeter.Measure` for `StringValue`, opaque IDs, paths, and URIs before `ResourceMeter.ChargeValue`/`ChargeBindingReturn` apply string-byte, allocation, and per-byte fuel accounting. Public hosts can pass `SandboxValue.FromString(...)` as entrypoint input or return it from a custom binding; those values are not limited by the JSON importer literal size cap.

The existing tests cover normal-sized string limits in `tests/SafeIR.Tests/Misc07/StringQuotaTests.cs` and binding return string charging in `tests/SafeIR.Tests/Misc01/BindingReturnCostTests.cs`, but they only use short strings and do not exercise byte-count overflow near `int.MaxValue / sizeof(char)`.

## Risk

For oversized public host values, the runtime can record an incorrect `StringBytes` total and skip or undercharge `MaxTotalStringBytes`, `MaxAllocatedBytes`, and binding return per-byte fuel. In the worst wrap case, negative `shape.StringBytes` also bypasses the `shape.StringBytes > 0` allocation charge and can reduce the accumulated string-byte counter, weakening later quota enforcement in the same run.

## Suggested test

Add focused string-byte overflow coverage around the public value/binding-return path. A practical regression can factor the byte-count conversion into an internal helper and assert that `int.MaxValue / sizeof(char) + 1` fails with `SandboxErrorCode.QuotaExceeded`, then cover both call sites through smaller integration tests to ensure they use the checked helper. If the test environment supports very-large strings, add an integration case where a binding returns `SandboxValue.FromString(new string('x', int.MaxValue / 2 + 1))` under high length limits and assert the result fails closed rather than reporting a wrapped or negative `ResourceUsage.StringBytes`.

## Expected behavior

All string byte accounting should widen before multiplication and fail closed on overflow. `ResourceMeter.ChargeString`, `SandboxLiteralConstraints.TextShape`, and any future string shape helpers should use checked `long` arithmetic equivalent to `checked((long)value.Length * sizeof(char))` and map overflow to `SandboxErrorCode.QuotaExceeded`.

## Deduplication key

`correctness/resource-meter/string-byte-count-overflow-before-widening`
