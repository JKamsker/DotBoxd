# Security Review Checklist

<!-- release-gate: required -->

## User input

- [x] Users cannot upload raw DLLs.
- [x] Users cannot upload raw MSIL.
- [x] Users cannot provide CLR type names.
- [x] Users cannot provide assembly-qualified names.
- [x] Users cannot provide metadata tokens.
- [x] Users cannot register bindings.
- [x] Users cannot grant capabilities.

## IR

- [x] IR has a version.
- [x] IR is canonicalized before hashing.
- [x] Unknown operations are rejected.
- [x] Unknown types are rejected.
- [x] Forbidden types are impossible or rejected.
- [x] Loops are fuel-accounted.
- [x] Recursion is disabled or depth-limited.
- [x] Huge constants are rejected.

## Type system

- [x] No `object`.
- [x] No `dynamic`.
- [x] No `Type`/reflection values.
- [x] No `Delegate`/function pointer values.
- [x] No `Stream`/`HttpClient`/`DbContext`.
- [x] No raw domain entities with behavior.
- [x] Opaque IDs are used for domain references.

## Bindings

- [x] Every binding has a stable ID and version.
- [x] Every binding has argument/return sandbox types.
- [x] Every binding declares effects.
- [x] Side-effecting bindings require capabilities.
- [x] Every binding has a cost model.
- [x] Unsafe signatures are rejected by registry validation.
- [x] Binding implementation sanitizes exceptions.
- [x] Binding implementation accepts cancellation/timeouts where applicable.
- [x] Binding implementation emits required audit events.

## Capabilities/policy

- [x] Default policy denies IO/network/game mutation.
- [x] Script capability requests are not grants.
- [x] Policy hash is included in execution plan.
- [x] Policy hash is included in compiled cache key.
- [x] Capability revocation invalidates plans/cache.
- [x] Deterministic mode rejects/replaces nondeterministic effects.

## Interpreter

- [x] Interpreter uses verified execution plan.
- [x] Interpreter charges fuel.
- [x] Interpreter enforces allocation/collection quotas.
- [x] Interpreter routes host calls through binding registry.
- [x] Interpreter supports cancellation.
- [x] Interpreter emits audit events.

## Compiler

- [x] Compiler uses verified execution plan.
- [x] Compiler emits only approved runtime calls.
- [x] Compiler injects fuel checks.
- [x] Compiler does not emit mutable static fields.
- [x] Compiler does not emit static constructors.
- [x] Compiler does not emit direct IO/network/reflection calls.
- [x] Compiler output is verified before loading.

## Verifier

- [x] Assembly refs allowlisted.
- [x] Type refs allowlisted.
- [x] Member refs allowlisted.
- [x] P/Invoke rejected.
- [x] Dangerous opcodes rejected.
- [x] Mutable static fields rejected.
- [x] Unexpected resources rejected.
- [x] Verifier runs before load.
- [x] Cached DLLs are reverified or verification cache is keyed by verifier version/allowlist.

## Cache

- [x] Cache key includes IR hash.
- [x] Cache key includes policy hash.
- [x] Cache key includes binding manifest hash.
- [x] Cache key includes compiler/verifier/runtime versions.
- [x] Cache writes are atomic.
- [x] Cache entries are hash-verified.
- [x] Untrusted users cannot write cache files.

## Resource limits

- [x] Fuel limit enforced.
- [x] Wall-time/cancellation enforced.
- [x] Allocation/collection quotas enforced cooperatively.
- [x] File/network byte limits enforced by facades.
- [x] Log/event limits enforced.
- [x] Worker process boundary requests fail closed unless a hardened worker client is configured.

## Audit

- [x] Run summary logged.
- [x] Policy denials logged.
- [x] Binding calls logged as required.
- [x] Verifier failures logged.
- [x] Cache integrity failures logged.
- [x] Logs sanitize secrets and sensitive paths.
