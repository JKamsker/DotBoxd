# DotBoxd.Kernels Error Code Reference

This is the maintained per-code reference for `DotBoxd.Kernels.SandboxErrorCode`. Every public code has
exactly one entry below. The order matches the enum in `src/DotBoxd.Kernels/Sandbox/SandboxError.cs`.

A `SandboxError` carries the `Code`, a tenant-safe `SafeMessage`, and an optional `DiagnosticId`
that correlates to host-side audit events. Hosts must surface only the safe message to untrusted
callers and never leak raw exceptions, secrets, full paths, connection strings, stack traces, or
internal object details.

## How to read each entry

Each entry documents:

- **Meaning** - what the code asserts about the run.
- **Likely causes** - the common conditions that produce it.
- **Retry** - whether retrying the same request unchanged can succeed (retryability).
- **Safe message** - what the tenant-safe user message should convey.
- **Audit/event** - the audit/event expectation a host should record.
- **Escalation** - when to escalate to admins/operators.

A retry that is not safe to repeat unchanged is marked **No**. "After change" means a retry can
succeed only once the input, policy, capability grant, or environment changes.

---

## ValidationError

- **Meaning**: The submitted JSON IR failed import, type checking, or effect validation before any
  execution plan was produced.
- **Likely causes**: Malformed JSON IR, forbidden types or operations, effects without a matching
  capability declaration, or canonicalization failures.
- **Retry**: No. The same IR fails the same way; retry only after the IR is corrected.
- **Safe message**: Tell the caller the submitted program was rejected by validation, without
  echoing the offending IR or internal validation internals.
- **Audit/event**: Record a validation-rejection event with the module hash and the
  `DiagnosticId`; the IR never executed.
- **Escalation**: Not an operator incident on its own. Escalate only on a sustained spike, which
  can indicate a probing or fuzzing client.

## PolicyDenied

- **Meaning**: The IR was well-formed but the capability policy denied the requested effect or
  binding for this tenant or module. Treat as a policy/security event.
- **Likely causes**: A required capability was not granted, a grant was revoked, or the module
  requested an effect outside its allowed policy.
- **Retry**: No while the policy is unchanged. After change: retry can succeed once the host grants
  the capability.
- **Safe message**: Tell the caller the operation is not permitted by policy. Do not reveal which
  capability or grant is missing.
- **Audit/event**: Record a `PolicyDenied` audit event with the tenant, module hash, and requested
  effect/binding.
- **Escalation**: Escalate repeated `PolicyDenied` for one tenant or module, as the runbook alerts
  call out, because it can indicate misconfiguration or an attack.

## PermissionDenied

- **Meaning**: A host binding refused the call at runtime because a capability was missing or a
  resource fell outside the granted scope (for example a path outside the allowed root).
- **Likely causes**: Missing capability at call time, path or target outside the granted root, or
  an allowlist that does not cover the requested resource.
- **Retry**: No while the grant is unchanged. After change: retry can succeed once the grant or
  target is corrected.
- **Safe message**: Tell the caller the requested resource or action is not allowed. Do not echo
  the path, host, or grant details.
- **Audit/event**: Record the denied binding call with the binding name and `DiagnosticId`; no
  side effect occurred.
- **Escalation**: Escalate a sustained pattern against one tenant as a possible scope-probing
  attempt.

## NotFound

- **Meaning**: A host binding completed the lookup but the requested resource does not exist.
- **Likely causes**: A missing file, record, or key behind a safe binding facade.
- **Retry**: After change. Retrying unchanged stays `NotFound` until the resource exists.
- **Safe message**: Tell the caller the requested item was not found, without revealing the
  resolved path or storage location.
- **Audit/event**: Record per the binding's `AuditLevel`; this is a normal read outcome, not a
  failure of the sandbox boundary.
- **Escalation**: None for routine misses. Investigate only if expected resources are
  unexpectedly absent across many requests.

## InvalidInput

- **Meaning**: Inputs supplied to a safe API or binding were rejected at the runtime boundary
  (distinct from `ValidationError`, which rejects the IR before execution).
- **Likely causes**: Out-of-range arguments, oversized payloads below quota but outside the API
  contract, or malformed values passed to a facade.
- **Retry**: After change. Retrying unchanged repeats the rejection.
- **Safe message**: Tell the caller the input was invalid. Do not include the rejected value if it
  may contain sensitive data.
- **Audit/event**: Record at the binding's audit level; no side effect occurred.
- **Escalation**: None routinely. A sustained spike can indicate a misbehaving client.

## QuotaExceeded

- **Meaning**: The run exceeded a resource quota such as fuel, output size, file size, log volume,
  or another metered limit.
- **Likely causes**: A program that loops or allocates beyond budget, oversized binding output, or
  a quota tuned too low for the workload.
- **Retry**: After change. Retrying unchanged hits the same limit; a smaller workload or a host
  quota change is required.
- **Safe message**: Tell the caller the operation exceeded its allowed resource budget, without
  exposing the exact limit values.
- **Audit/event**: Record the quota-exhaustion event with the metric that tripped and the
  `DiagnosticId`.
- **Escalation**: Escalate quota-exhaustion spikes, as listed in the runbook alerts, to
  distinguish abuse from undersized limits.

## Timeout

- **Meaning**: The run or a binding call exceeded its wall-time limit.
- **Likely causes**: Long-running IR, a slow or hung host binding, or contention in the host
  environment.
- **Retry**: Transient timeouts may be retried with backoff; a deterministic timeout from the same
  IR is effectively **No** until the workload or limit changes.
- **Safe message**: Tell the caller the operation timed out. Do not reveal internal timing or the
  failing component.
- **Audit/event**: Record the timeout with the elapsed budget and `DiagnosticId`; treat a worker
  timeout as fail-closed.
- **Escalation**: Escalate worker timeouts and repeated timeouts, per the worker incident
  response, as possible infrastructure failures.

## Cancelled

- **Meaning**: The run was cancelled, typically through a cancellation token or host shutdown.
- **Likely causes**: Caller cancellation, host draining or recycling, or a cooperating shutdown
  signal.
- **Retry**: Yes once the cancellation reason has cleared.
- **Safe message**: Tell the caller the operation was cancelled and can be retried later.
- **Audit/event**: Record cancellation with the `DiagnosticId`; partial side effects must be
  reported per binding audit.
- **Escalation**: None for expected cancellations. Investigate unexpected cancellation storms,
  which can indicate host instability.

## BindingFailure

- **Meaning**: A host binding threw an unexpected exception that was converted to a sandbox error
  with a safe message.
- **Likely causes**: A bug or unhandled condition in a host binding facade, or a downstream
  dependency failure surfaced through a binding.
- **Retry**: Transient dependency failures may be retried with backoff; a deterministic binding
  bug is **No** until the host fixes the binding.
- **Safe message**: Tell the caller the operation could not be completed, without exposing the
  underlying exception, stack trace, or dependency details.
- **Audit/event**: Record a `BindingFailure` audit event with the binding name and `DiagnosticId`;
  the original exception stays host-side only.
- **Escalation**: Escalate repeated binding failures to the binding owner; audited binding
  failures are a tracked runbook signal.

## VerifierFailure

- **Meaning**: The generated-code verifier rejected a compiled artifact, or generated assembly
  emission was rejected. Treat as a security event.
- **Likely causes**: A compiler or verifier defect, a verifier/compiler version mismatch, or
  emitted code that violates the post-emit safety contract.
- **Retry**: No. Do not retry compiled execution; the host may fall back to interpreted mode for
  the affected tenants.
- **Safe message**: Tell the caller the program could not be executed safely. Do not reveal
  verifier internals.
- **Audit/event**: Record a `VerifierFailure` audit event and preserve the artifact for
  investigation per the verifier/cache incident response.
- **Escalation**: Escalate immediately. Disable compiled execution for affected tenants and follow
  the verifier incident response; rotate the verifier/compiler identity if unsafe output was
  accepted.

## CacheInvalid

- **Meaning**: A compiled-cache entry failed identity validation and was rejected. Treat cache
  integrity failures as security events.
- **Likely causes**: Stale or corrupted cache entries, a cache key or identity mismatch after a
  version change, or tampered cache files.
- **Retry**: After change. The run can proceed once the entry is invalidated and recompiled or
  interpreted mode is selected.
- **Safe message**: Tell the caller the operation could not be served; recompilation will occur
  transparently where applicable.
- **Audit/event**: Record a `CacheInvalidated` event with the cache key and identity hashes per
  the cache incident response.
- **Escalation**: Escalate `CacheInvalidated` spikes or repeated hash mismatches, as the runbook
  alerts call out; quarantine affected cache roots before deletion.

## HostFailure

- **Meaning**: An internal host or infrastructure failure prevented the run from completing through
  the normal boundary, including malformed or mismatched worker results.
- **Likely causes**: Host process or worker infrastructure faults, mismatched module/plan/policy
  identity from a worker, or unexpected host-side errors outside binding code.
- **Retry**: Transient infrastructure faults may be retried with backoff after recovery; a
  persistent mismatch is **No** until the host deployment is reconciled.
- **Safe message**: Tell the caller a temporary internal error occurred and the request may be
  retried later. Do not expose host internals.
- **Audit/event**: Record a host-failure event with the `DiagnosticId`; keep the host-side
  fail-closed result and preserve worker stdout/stderr and exit metadata.
- **Escalation**: Escalate as a host-infrastructure failure, especially repeated malformed worker
  results, per the worker incident response.

---

## Maintenance

This reference is gated by a regression test that parses `SandboxErrorCode` and verifies that every
enum value has an entry here with retry, audit/event, escalation, and safe-message guidance. When a
new error code is added to `src/DotBoxd.Kernels/Sandbox/SandboxError.cs`, add its entry here in the same
order so new codes cannot ship undocumented.
