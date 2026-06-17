# 15 — Testing and Red-Team Plan

## Test strategy

Testing must cover:

- JSON importer addon
- canonicalization
- type checker
- effect analyzer
- policy resolver
- binding registry validation
- interpreter
- compiler
- generated-code verifier
- cache invalidation
- resource limits
- safe API facades
- audit output
- interpreter/compiler parity

## Golden tests

For each valid sample module, store expected:

```text
canonical IR
module hash
inferred effects
diagnostics
execution result
fuel usage range
binding calls
```

## Invalid IR tests

Reject:

- unknown operation
- duplicate function ID
- invalid local reference
- invalid block target
- invalid type
- forbidden type
- wrong binding arity
- wrong binding argument type
- missing return
- unbounded recursion if disabled
- huge constants
- collection nesting over limit

## Policy tests

Cases:

- pure module runs with default policy
- file read denied without `file.read`
- file read allowed with scoped root
- file read denied outside root
- game write denied with read-only policy
- network denied by default
- deterministic mode rejects clock/random/network
- capability revocation invalidates plan/cache

## Binding registry tests

Reject bindings that:

- return `object`
- accept `Type`
- accept `IServiceProvider`
- return `Stream`
- expose `HttpClient`
- expose mutable host entity
- have side effects but no capability
- have unknown effect
- have missing cost model
- have duplicate ID/version conflict

## Safe file API tests

Test paths:

```text
config.json                    allowed
./config.json                  denied; path literals must already be canonical
sub/../config.json             denied; path literals must not contain traversal segments
../secret.txt                  denied
/rooted/path                   denied
C:\Windows\win.ini            denied on Windows
\\server\share\x             denied on Windows
file:///etc/passwd             denied
config/../../secret.txt        denied
```

Test:

- max file size
- max total bytes
- disallowed extension
- symlink/junction/reparse point behavior
- concurrent writes if write allowed
- atomic write behavior
- sanitized audit logs

## Safe network tests

Test:

- host allowlist
- scheme allowlist
- IP literal rejection
- localhost/private IP rejection unless explicitly allowed
- redirect to forbidden host
- DNS rebinding strategy if implemented
- response size limit
- timeout
- audit sanitization

## Resource tests

Test:

- infinite loop fuel exhaustion
- deep recursion rejected or call-depth exceeded
- huge list growth allocation quota
- huge string concatenation quota
- too many host calls
- too many log events
- cancellation token stops execution
- worker process killed on hard timeout

## Interpreter tests

Test:

- every opcode/instruction
- function calls
- loops
- errors
- binding calls
- debug stepping
- trace output
- deterministic replay

## Compiler tests

Test:

- generated DLL loads after verification
- entrypoint delegate works
- fuel checks injected into loops
- host calls route through stubs
- no unexpected assembly references
- no mutable static fields
- cache artifacts written atomically
- compilation failure falls back only when allowed

## Differential tests

For every valid nontrivial module:

```text
result(interpreter) == result(compiled)
audited effects(interpreter) == audited effects(compiled)
resource usage within tolerance
```

For nondeterministic APIs, inject deterministic clock/random/network fixtures.

## Verifier malicious fixture tests

Build or handcraft assemblies that attempt:

- `System.IO.File.ReadAllText`
- `System.Net.Http.HttpClient`
- `System.Reflection.Assembly.Load`
- `System.Type.GetType`
- `MethodInfo.Invoke`
- `Activator.CreateInstance`
- `Environment.GetEnvironmentVariable`
- `Process.Start`
- `Thread.Start`
- `Task.Run`
- P/Invoke via `DllImport`
- `calli`
- `ldtoken`
- `ldftn`
- function pointers
- mutable static field
- static constructor
- embedded resource
- exception handler if forbidden
- raw `Stream`
- `IServiceProvider.GetService`

All must be rejected.

## Fuzzing

Fuzz:

- JSON importer addon
- IR deserializer
- type checker
- policy resolver
- interpreter
- verifier input DLLs if feasible

Properties:

- no host crash
- invalid input rejected
- no unhandled exceptions to user
- no hangs without timeout
- deterministic canonicalization

## Cache tests

Test:

- policy hash change invalidates cache
- binding manifest change invalidates cache
- compiler version change invalidates cache
- runtime facade hash change invalidates cache
- corrupted DLL rejected
- corrupted manifest rejected
- DLL/manifest mismatch rejected
- artifact written by untrusted user not accepted
- stale cache not used after revocation

## Audit tests

Assert logs include:

- run ID
- module hash
- policy hash
- execution mode
- cache status
- effects used
- binding calls
- resource usage
- quota failures

Assert logs do not include:

- secrets
- full sensitive host paths
- raw auth headers
- connection strings

## CI gates

Required gates before release:

- all unit tests
- all verifier rejection fixtures
- interpreter/compiler differential suite
- path traversal tests on target OSes
- resource-limit tests
- cache invalidation tests
- security checklist review

The normal CI workflow may report open checklist items while implementation is still in progress.
Release branches or release jobs must run `eng/scripts/check-release-readiness.ps1 -RequireComplete`
and may only pass when all release-gated checklist items are closed. Inventory-only items remain
visible for host operators and production deployment planning but do not block package publication.

## Red-team scenarios

### Scenario 1: API escape

Attacker tries to invoke reflection through any exposed value.

Expected:

- no value exposes `Type`/`object`/`Delegate`
- verifier rejects reflection references

### Scenario 2: File escape

Attacker attempts path traversal and symlink escape.

Expected:

- safe file API denies
- audit records denied sanitized path

### Scenario 3: Cache confusion

Attacker compiles under permissive policy and executes under restrictive policy.

Expected:

- policy hash mismatch prevents cache reuse

### Scenario 4: Interpreter/compiler mismatch

Attacker creates IR that behaves safely in interpreter but compiles to unsafe behavior due to compiler bug.

Expected:

- verifier catches unsafe references/opcodes
- differential tests catch semantic mismatch

### Scenario 5: Resource DoS

Attacker writes infinite loop or huge allocation.

Expected:

- fuel/allocation limit stops execution
- worker process boundary kills if needed

## Scenario coverage map

| Scenario | Primary test artifacts |
|---|---|
| API escape | `HostValueBoundaryTests`, `BindingRegistryHardeningTests`, `VerifierAttackMatrixTests`, `VerifierDocumentedAttackMatrixTests` |
| File escape | `SafeFileSystemTests`, `SafeFileSystemReparsePointTests`, `FileExtensionPolicyTests`, `PathUriLiteralValidationTests` |
| Cache confusion | `CompiledCacheTests`, `CompiledCacheMetadataTests`, `CompiledCacheRootGuardTests`, `CacheKeyIdentityTests`, `CapabilityRevocationTests` |
| Interpreter/compiler mismatch | `DifferentialFuzzTests`, `DifferentialFeatureCoverageTests`, `CompiledArtifactGuardTests`, `CompilerTests` |
| Resource DoS | `InterpreterAndPolicyTests`, `ResourceMeterTests`, `ResourceScanCancellationTests`, `CollectionQuotaTests`, `CollectionFuelAccountingTests`, `WorkerIsolationTests` |

The release gate runs these through the unit-test suite and the required security-boundary test
script. When a scenario gains a new bypass class, add the fixture first and then update this map so
the release review can trace the scenario to executable coverage.
