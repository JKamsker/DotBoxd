# 02 — Threat Model

## Assets to protect

The sandbox must protect:

- host process integrity
- host filesystem
- network access
- environment variables/secrets
- database connections
- game state or business state
- other tenants' data
- host CPU/memory availability
- plugin/module cache integrity
- audit log integrity
- runtime configuration and binding registry

## Attackers

### A1. Normal user script author

A normal user may accidentally write inefficient or invalid logic.

Expected behavior:

- reject invalid IR with useful diagnostics
- stop runaway code with fuel/time limits
- expose safe errors, not host exceptions with secrets

### A2. Malicious script author

A malicious user intentionally tries to escape the sandbox.

They may try to:

- call forbidden .NET APIs
- construct type names dynamically
- use reflection indirectly
- smuggle objects through host APIs
- trigger host exceptions that leak data
- allocate too much memory
- create infinite loops
- exploit path traversal
- exploit symlinks/reparse points
- force cache confusion
- cause verifier/compiler mismatch
- find differences between interpreter and compiled mode

### A3. Malicious binding author/admin mistake

A host developer may accidentally expose an unsafe binding.

Examples:

- returning `object`
- accepting `Type`
- exposing `IServiceProvider`
- exposing raw `Stream`
- exposing `DbContext`
- exposing `HttpClient`
- exposing `FileInfo`
- exposing `Delegate`
- exposing mutable host domain objects

The binding system must make unsafe binding signatures difficult to register and easy to detect.

### A4. Cache attacker

An attacker tries to reuse or replace cached DLLs or verified plans.

Examples:

- replace DLL file on disk
- reuse cache entry compiled under older policy
- compile with permissive binding version, execute under restrictive policy
- exploit path traversal in cache keys
- inject a fake manifest

### A5. Hostile tenant / high-risk environment

A tenant may intentionally attempt denial-of-service or escape via runtime bugs.

For this attacker class, in-process sandboxing is not enough. Use a worker process/container/OS security boundary.

## Trust boundaries

```text
[Serialized IR]    untrusted
       |
       v
[Importer addon]   trusted code consuming untrusted input
       |
       v
[IR validator]     trusted enforcement
       |
       v
[Execution plan]   trusted artifact, hashable
       |
       +--> [Interpreter]       trusted backend
       |
       +--> [Compiled backend]  trusted backend
                |
                +--> [DynamicMethod delegate] gated before invocation
                |
                +--> [Generated DLL]     untrusted until post-verified
                |
                v
            [Verifier]          trusted enforcement
                |
                v
            [Loaded assembly]   trusted only after verification, still not OS-isolated
```

## Assumptions

The in-process model assumes:

- importer addons, validator, interpreter, compiler, verifier, runtime facades, and binding registry are trusted
- users cannot upload raw DLLs/MSIL
- users cannot configure granted capabilities
- all host API access goes through registered bindings
- generated assemblies are verified before loading/execution
- generated IL is invoked only through a compiled runtime form such as a gated `DynamicMethod`
  delegate or a loaded verified assembly
- generated assemblies are not modified after verification
- the host process itself is not compromised

## Security invariants

### I1. No arbitrary CLR references from user input

User JSON IR cannot contain raw CLR type names, assembly names, metadata tokens, method handles, delegates, or function pointers.

### I2. Binding grants are host-controlled

A script may declare:

```json
{ "capabilityRequests": [{ "id": "file.read" }] }
```

But only host policy can grant:

```text
grant file.read root=/tenant/123/data readonly=true
```

### I3. No raw host objects cross into user code

The sandbox should pass safe values, handles, or opaque IDs, not host objects.

Forbidden boundary types:

- `object`
- `dynamic`
- `Type`
- `Assembly`
- `MemberInfo`
- `MethodInfo`
- `Delegate`
- `Expression`
- `IServiceProvider`
- `Stream`
- `DbContext`
- `HttpClient`
- `IntPtr`
- `SafeHandle`
- raw domain entities with behavior

### I4. Compiled runtime forms must be gated before execution

Even if the compiler is trusted, generated assemblies are treated as untrusted until inspected.
`DynamicMethod` backends must apply equivalent allowlist gating before delegate invocation.

### I5. Cache entries are not trusted by path

A cached DLL is valid only if:

- its content hash matches its manifest
- its manifest hash matches the expected cache key
- the verifier passes against the current verifier and policy
- the binding/runtime versions match

### I6. Effects cannot be hidden in pure bindings

Every binding must declare effects. A binding that touches file/network/game state cannot be marked pure.

### I7. Resource limits are enforced at the IR operation level

Loops, calls, allocations, host calls, and collection growth must charge budget.

### I8. Process boundary for hard isolation

When hard isolation is required, in-process restrictions are not enough. Execute the sandbox in a worker process/container with OS-level resource and permission limits.

## Out-of-scope attacks

The following are not solved by the IR sandbox alone:

- CLR/JIT vulnerabilities
- CPU side channels
- host process compromise
- malicious host bindings deliberately exposing power
- OS kernel vulnerabilities
- denial-of-service that exceeds cooperative resource limits before checks run
- bugs in native dependencies called by safe facades

## Threat mitigation table

| Threat | Primary mitigation | Defense in depth |
|---|---|---|
| Reflection escape | No reflection ops/types in IR; verifier blocks references | Process isolation |
| P/Invoke/native escape | IR cannot express; verifier blocks `ImplMap`/PInvoke | Process isolation |
| File path traversal | Safe file facade canonicalizes and scopes paths | Worker account with limited filesystem |
| Network exfiltration | No network by default; safe network facade allowlists endpoints | Egress firewall |
| Infinite loop | Fuel checks on loops/calls | Worker timeout/kill |
| Memory exhaustion | Safe collections + allocation budgets | OS memory limit |
| Cache confusion | Cache key includes policy/bindings/runtime/verifier | Reverify cached DLL |
| Interpreter/compiler mismatch | Differential tests | Prefer interpreter for critical small scripts |
| Unsafe binding | Binding signature validation | Manual security review checklist |
