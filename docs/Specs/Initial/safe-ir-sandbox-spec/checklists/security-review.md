# Security Review Checklist

## User input

- [ ] Users cannot upload raw DLLs.
- [ ] Users cannot upload raw MSIL.
- [ ] Users cannot provide CLR type names.
- [ ] Users cannot provide assembly-qualified names.
- [ ] Users cannot provide metadata tokens.
- [ ] Users cannot register bindings.
- [ ] Users cannot grant capabilities.

## IR

- [ ] IR has a version.
- [ ] IR is canonicalized before hashing.
- [ ] Unknown operations are rejected.
- [ ] Unknown types are rejected.
- [ ] Forbidden types are impossible or rejected.
- [ ] Loops are fuel-accounted.
- [ ] Recursion is disabled or depth-limited.
- [ ] Huge constants are rejected.

## Type system

- [ ] No `object`.
- [ ] No `dynamic`.
- [ ] No `Type`/reflection values.
- [ ] No `Delegate`/function pointer values.
- [ ] No `Stream`/`HttpClient`/`DbContext`.
- [ ] No raw domain entities with behavior.
- [ ] Opaque IDs are used for domain references.

## Bindings

- [ ] Every binding has a stable ID and version.
- [ ] Every binding has argument/return sandbox types.
- [ ] Every binding declares effects.
- [ ] Side-effecting bindings require capabilities.
- [ ] Every binding has a cost model.
- [ ] Unsafe signatures are rejected by registry validation.
- [ ] Binding implementation sanitizes exceptions.
- [ ] Binding implementation accepts cancellation/timeouts where applicable.
- [ ] Binding implementation emits required audit events.

## Capabilities/policy

- [ ] Default policy denies IO/network/game mutation.
- [ ] Script capability requests are not grants.
- [ ] Policy hash is included in execution plan.
- [ ] Policy hash is included in compiled cache key.
- [x] Capability revocation invalidates plans/cache.
- [ ] Deterministic mode rejects/replaces nondeterministic effects.

## Interpreter

- [ ] Interpreter uses verified execution plan.
- [ ] Interpreter charges fuel.
- [ ] Interpreter enforces allocation/collection quotas.
- [ ] Interpreter routes host calls through binding registry.
- [ ] Interpreter supports cancellation.
- [ ] Interpreter emits audit events.

## Compiler

- [ ] Compiler uses verified execution plan.
- [ ] Compiler emits only approved runtime calls.
- [ ] Compiler injects fuel checks.
- [ ] Compiler does not emit mutable static fields.
- [ ] Compiler does not emit static constructors.
- [ ] Compiler does not emit direct IO/network/reflection calls.
- [ ] Compiler output is verified before loading.

## Verifier

- [ ] Assembly refs allowlisted.
- [ ] Type refs allowlisted.
- [ ] Member refs allowlisted.
- [ ] P/Invoke rejected.
- [ ] Dangerous opcodes rejected.
- [ ] Mutable static fields rejected.
- [ ] Unexpected resources rejected.
- [ ] Verifier runs before load.
- [ ] Cached DLLs are reverified or verification cache is keyed by verifier version/allowlist.

## Cache

- [ ] Cache key includes IR hash.
- [ ] Cache key includes policy hash.
- [ ] Cache key includes binding manifest hash.
- [ ] Cache key includes compiler/verifier/runtime versions.
- [ ] Cache writes are atomic.
- [ ] Cache entries are hash-verified.
- [ ] Untrusted users cannot write cache files.

## Resource limits

- [ ] Fuel limit enforced.
- [ ] Wall-time/cancellation enforced.
- [ ] Allocation/collection quotas enforced cooperatively.
- [ ] File/network byte limits enforced by facades.
- [ ] Log/event limits enforced.
- [ ] Worker process boundary used for high-risk tenants.

## Audit

- [ ] Run summary logged.
- [ ] Policy denials logged.
- [ ] Binding calls logged as required.
- [ ] Verifier failures logged.
- [ ] Cache integrity failures logged.
- [ ] Logs sanitize secrets and sensitive paths.
