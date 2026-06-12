# 14 — Observability and Audit

## Purpose

The sandbox should be observable for debugging, performance tuning, abuse detection, and security review.

## Run identity

Every execution gets a run ID.

```text
runId = globally unique ID
moduleHash = canonical IR hash
planHash = execution plan hash
policyHash = policy hash
```

## Required audit fields

```text
runId
tenantId optional
userId/scriptAuthor optional
moduleId/moduleHash
policyId/policyHash
bindingManifestHash
executionMode
cacheStatus
timestamp
success/failure
errorCode optional
fuelUsed
maxFuel
allocationCharged
hostCalls
artifactHash optional
```

The current `RunSummary` event stores these as structured string fields where possible. Duration,
completed-at timestamps, and capability-grant/use rollups are recommended operational extensions,
not required fields in the current public `SandboxAuditEvent` shape.
Hosts can attach an operational audit observer with `SandboxHostBuilder.ForwardAuditEventsTo(...)`.
The observer receives the same sequenced events returned in `SandboxExecutionResult.AuditEvents`,
so callers can forward them to retention, metrics, tracing, and alerting pipelines without
changing execution behavior.

## Binding audit events

For each audited binding call:

```text
runId
sequenceNumber
bindingId
capabilityId
effect
resourceKind
resourceId sanitized
startedAt
duration optional
success/failure
bytesRead/bytesWritten optional
errorCode optional
```

Runtime enforcement:

- `AuditLevel.None` bindings do not require binding audit events.
- Any other `AuditLevel` requires a non-debug audit event with the binding ID.
- A successful binding call must emit a successful audit event before the call is accepted.
- A failed binding call must leave a failed audit event. If the binding fails before it can emit
  one, the runtime writes a sanitized `BindingCall` failure event and preserves the original
  sandbox error.
- Interpreted debug traces are diagnostics only and never satisfy binding audit requirements.

## Sanitization

Audit logs should be useful but not leak secrets.

Avoid logging:

- raw file contents
- auth headers
- access tokens
- connection strings
- full host filesystem paths if sensitive
- raw exception messages containing secrets
- personal data unless necessary and allowed

Use sanitized resource IDs:

```text
tenant://123/config/settings.json
https://api.example.com/path without query secrets
player:5751
item:quest_reward_001
```

## Metrics

Recommended metrics:

```text
sandbox.runs.total
sandbox.runs.failed
sandbox.runs.duration
sandbox.fuel.used
sandbox.quota.exceeded
sandbox.policy.denied
sandbox.cache.hit
sandbox.cache.miss
sandbox.cache.invalid
sandbox.compile.duration
sandbox.verify.duration
sandbox.interpreter.duration
sandbox.binding.calls
sandbox.binding.duration
sandbox.worker.killed
```

Labels:

```text
executionMode
bindingId
errorCode
policyId
tenant/trust-zone if safe
```

Avoid high-cardinality labels like raw module hash unless metrics system can handle it.

## Tracing

Trace spans:

```text
Sandbox.Prepare
Sandbox.Parse
Sandbox.Validate
Sandbox.ResolvePolicy
Sandbox.Interpret
Sandbox.Compile
Sandbox.Verify
Sandbox.LoadAssembly
Sandbox.Execute
Sandbox.BindingCall
```

## Debug traces

For interpreted mode, optional debug traces:

```text
instruction executed
JSON location or IR node ID
local values summarized
fuel remaining
binding call
```

These should be disabled in production by default or heavily limited.

## Security alerts

Alert on:

- verifier failure
- generated assembly rejected
- cache hash mismatch
- repeated policy-denied attempts
- repeated quota exhaustion
- unexpected binding exception
- worker process killed
- forbidden path/network attempts
- compiled/interpreted differential test failure in CI

## Audit retention

Retention depends on product needs, but security-relevant events should outlive cache entries.

Suggested minimum:

- execution summary: 30-90 days
- policy denials: 90 days
- verifier/cache integrity failures: 180+ days
- high-risk binding calls: business/security policy dependent

## User-facing diagnostics

Keep user-facing errors clear but safe.

Good:

```text
file.readText denied: path is outside the granted sandbox root.
```

Bad:

```text
UnauthorizedAccessException at C:\prod\secrets\tenant-123\db-password.txt stack trace...
```

## Developer diagnostics

Trusted admins can access richer diagnostics:

- internal exception IDs
- stack traces
- verifier diagnostics
- cache paths
- binding config

Still avoid secrets by default.

## Abuse detection

Track per user/module:

- quota exhaustion rate
- policy-denied operation attempts
- repeated path traversal attempts
- repeated network blocked hosts
- high compile/cache churn
- large log output
- high memory budget usage

Use this to throttle or require review.
