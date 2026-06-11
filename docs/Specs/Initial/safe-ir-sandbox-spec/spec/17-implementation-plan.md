# 17 — Implementation Plan

## Principle

Build the sandbox in layers. Do not start with IL generation. The interpreter, validator, policy model, and bindings are the real sandbox. The compiler is an optimization backend.

## Phase 0 — Core model

Deliver:

- sandbox type model
- IR model
- diagnostics model
- canonical serializer/hash
- JSON IR importer

Acceptance:

- invalid IR rejected
- canonicalization deterministic
- module hash stable across JSON whitespace and property-order differences

## Phase 1 — Validation and policy

Deliver:

- structural validator
- type checker
- effect analyzer
- capability policy resolver
- binding registry validation

Acceptance:

- missing capabilities rejected
- forbidden types rejected
- unsafe bindings rejected
- effect inference golden tests pass

## Phase 2 — Interpreted mode

Deliver:

- direct IR interpreter
- fuel checks
- binding invocation
- safe error model
- audit events
- no compiler, verifier, Reflection.Emit, `DynamicMethod`, DLL, or IL execution dependency

Acceptance:

- pure scripts execute
- file/game/network denied by default
- granted safe file read works
- infinite loops stop by fuel
- debug trace works for basic scripts
- interpreted execution does not create or consume compiled artifacts

## Phase 3 — Runtime safe APIs

Deliver:

- `SandboxContext`
- safe file API
- safe collection API
- safe clock/random API
- optional safe game-state snapshot/command API

Acceptance:

- path traversal tests pass
- quota tests pass
- no raw host objects exposed
- audit events sanitized

## Phase 4 — Compiler parity foundation

Deliver:

- compiler consumes the same verified IR semantics as the interpreter
- IR node IDs or JSON locations preserved for diagnostics
- differential test harness ready for compiled mode

Acceptance:

- interpreted and compiled backends agree for supported modules
- operation costs assigned at IR operation level

## Phase 5 — Compiled mode MVP

Deliver:

- compiler backend using runtime stubs
- compiled runtime form only; no interpreter IL layer
- generated assembly in memory/file
- simple entrypoint delegate
- no async generated code
- boxed `SandboxValue` representation

Acceptance:

- compiled pure scripts run
- compiled scripts call binding stubs only
- fuel checks injected
- interpreter/compiler differential tests pass

## Phase 6 — Generated-code verifier

Deliver:

- assembly metadata reader
- assembly/type/member allowlists
- opcode verifier
- P/Invoke/native rejection
- cache manifest verifier

Acceptance:

- malicious fixture assemblies rejected
- generated compiler output accepted
- verifier failure prevents load

## Phase 7 — Persistent DLL cache

Deliver:

- artifact manifest
- cache key generation
- atomic writes
- cache read verification
- invalidation on policy/bindings/runtime/compiler/verifier changes

Acceptance:

- cache hit works
- cache mismatch rejected
- policy revocation invalidates artifact
- corrupted cache quarantined

## Phase 8 — Worker process isolation optional but recommended

Deliver:

- worker process protocol
- restricted filesystem config
- wall-time watchdog
- memory/process kill strategy
- no secrets in worker environment

Acceptance:

- runaway worker can be killed
- worker has no access to host secrets
- results/audit returned to host

## Phase 9 — Performance improvements

Only after correctness:

- typed compiled locals
- specialized opcodes
- binding direct-call stubs
- hotness-based auto compile
- preverified artifact cache
- pooling internal interpreter frames/values

Acceptance:

- no verifier allowlist expansion without tests
- differential tests still pass
- resource charging remains comparable

## Recommended MVP cut

A useful MVP is:

```text
restricted IR
pure primitives
safe collections
capability/effect system
safe file read binding
interpreter only
fuel/time/alloc quotas
basic audit
```

This already proves the sandbox model.

Compiled mode should be added only when:

- the IR cannot express escapes
- binding system is stable
- interpreter semantics are tested
- verifier work is staffed seriously

## Definition of done for production

Production readiness requires:

- no arbitrary C#/IL/DLL user input
- deny-by-default policy
- complete binding review
- interpreter resource limits
- verifier for compiled mode
- cache invalidation tests
- audit logs
- process isolation for high-risk tenants
- red-team fixtures passing
- documented unsupported features
