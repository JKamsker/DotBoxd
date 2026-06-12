# Release Readiness Checklist

Sections marked `release-gate: required` are enforced by
`scripts/check-release-readiness.ps1 -RequireComplete`. Sections marked
`release-gate: inventory` are tracked for operators and production deployments but do not block
publishing the SafeIR packages.

## MVP release

<!-- release-gate: required -->

- [x] Restricted IR implemented.
- [x] Canonical hashing implemented.
- [x] Type checker implemented.
- [x] Effect analyzer implemented.
- [x] Capability policy implemented.
- [x] Binding registry validation implemented.
- [x] Interpreted mode implemented.
- [x] Fuel limits implemented.
- [x] Safe error model implemented.
- [x] Basic audit implemented.
- [x] At least one safe file binding implemented and tested.
- [x] Path traversal tests pass.
- [x] Binding security checklist passes.

## Compiled-mode release

<!-- release-gate: required -->

- [x] Compiler emits valid managed assemblies.
- [x] Generated assemblies use runtime stubs only.
- [x] Verifier implemented.
- [x] Verifier malicious fixtures pass.
- [x] Compiled/interpreted differential tests pass.
- [x] DLL cache manifest implemented.
- [x] Cache invalidation tests pass.
- [x] Cache corruption tests pass.
- [x] `AssemblyLoadContext` lifecycle tested.
- [x] Fallback behavior documented.

## Production hardening

<!-- release-gate: inventory -->

- [x] Worker delegation API fails closed unless a hardened worker profile is configured.
- [ ] Concrete hardened worker process/container implementation available for high-risk tenants.
- [ ] Worker has no secrets by default.
- [ ] Worker resource limits configured.
- [ ] Audit retention configured.
- [ ] Metrics dashboards configured.
- [ ] Security alerting configured.
- [x] Binding review process documented.
- [x] Capability grant process documented.
- [x] Red-team scenarios run.
- [ ] Incident response for verifier/cache failures documented.

## Documentation

<!-- release-gate: inventory -->

- [x] User-facing language docs.
- [x] Host binding author guide.
- [x] Security model docs.
- [x] Capability catalog.
- [x] Error code reference.
- [x] Debugging guide.
- [ ] Operational runbook.
