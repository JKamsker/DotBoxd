# DotBoxD.Kernels Operational Runbook

This runbook covers operating the DotBoxD.Kernels packages in a host application. It does not claim that a
host has deployed worker processes, dashboards, retention, or alerting; those are environment-owned
inventory items that must be configured by the product operator.

## Release Preflight

Before publishing or enabling DotBoxD.Kernels for a tenant:

- Run `dotnet test DotBoxD.slnx --configuration Release`.
- Run `eng/scripts/check-release-readiness.ps1 -RequireComplete`.
- Run `eng/scripts/check-spec-manifest.ps1`.
- Run `eng/scripts/check-docs-smoke.ps1`.
- Pack and validate packages with `eng/scripts/check-package-metadata.ps1`.
- Confirm package `RepositoryCommit` metadata matches the reviewed commit.
- Confirm all production inventory items that apply to the target tenant are either completed or
  explicitly accepted by the service owner.

## Deployment Modes

In-process execution is suitable only when cooperative resource accounting and host binding
facades are enough for the tenant risk profile. Use `SandboxIsolation.InProcess` and keep
compiled mode behind verifier and cache gates.

Worker-process execution is required for high-risk tenants or when the host needs an OS kill
boundary. `SandboxIsolation.WorkerProcess` must fail closed unless the host configures a worker
client with a hardened profile. A production worker should run as a separate OS identity or
container, inherit no secrets, have restricted filesystem permissions, and have memory, process,
wall-time, and network limits applied outside the DotBoxD.Kernels process.

## Normal Operations

Operators should retain `RunSummary`, policy denial, verifier failure, cache invalidation,
worker failure, and audited binding-call events according to product policy. Metrics should
include run count, failure count, fuel used, quota exhaustion, cache status, verifier failures,
worker failures, and audited binding failures.

Recommended alerts:

- `VerifierFailure` or generated assembly rejection.
- `CacheInvalidated` spikes or repeated cache hash mismatches.
- Repeated `PolicyDenied` for a tenant or module.
- Quota exhaustion spikes.
- Worker timeout, cancellation, or malformed worker-result failures.
- Binding audit enforcement failures.

## Verifier Or Cache Incident Response

Treat verifier and compiled-cache integrity incidents as security events.

1. Disable compiled execution for affected tenants by selecting interpreted mode or disabling the
   compiler registration in the host.
2. Quarantine affected compiled-cache roots. Preserve files for investigation before deletion.
3. Capture the commit SHA, package versions, verifier version, compiler version, runtime version,
   module hash, plan hash, policy hash, binding manifest hash, artifact hash, and cache key.
4. Export the relevant `RunSummary`, `VerifierFailure`, `CacheInvalidated`, and
   `CompiledExecutionFailed` audit events.
5. Reproduce with the exact package versions and the same canonical JSON IR, policy, and binding
   manifest.
6. If the verifier accepted unsafe output, block package rollout, rotate the verifier/compiler
   version identity, and invalidate compiled-cache entries that share the vulnerable identity.
7. If the cache accepted stale or corrupted data, fix cache identity validation before re-enabling
   compiled execution and delete or quarantine affected cache entries.
8. Run the verifier attack matrix, cache corruption tests, and differential interpreter/compiler
   tests before restoring compiled mode.
9. Document tenant impact and whether any side-effecting binding ran during the incident window.

## Worker Incident Response

If a worker times out, returns malformed audit, or returns mismatched module, plan, or policy
identity:

1. Keep the host-side fail-closed result.
2. Stop reusing the worker instance.
3. Preserve worker stdout/stderr, host audit events, and process/container exit metadata.
4. Recycle the worker pool only after confirming the worker image and DotBoxD.Kernels package set match the
   host deployment.
5. Escalate repeated malformed worker results as host-infrastructure failures.

## Recovery Criteria

Do not re-enable a failed backend or worker pool until:

- the failing condition is reproduced or confidently explained,
- the relevant tests pass against the candidate fix,
- affected cache entries are invalidated,
- production inventory items required for the tenant risk profile are present, and
- the service owner accepts any residual risk in writing.
