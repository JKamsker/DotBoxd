# 11 — Generated Code Verifier

## Purpose

The compiler is trusted, but bugs happen. The generated assembly must be verified before it is loaded or executed.

The verifier proves the emitted DLL only references the approved runtime surface and contains no forbidden metadata or instructions.

## Verification input

Verifier receives:

- DLL bytes
- optional PDB bytes
- expected artifact manifest
- execution plan hash
- policy hash
- binding manifest hash
- allowed assembly/type/member/opcode lists
- verifier version

## Verification output

```csharp
public sealed record VerificationResult(
    bool Succeeded,
    IReadOnlyList<VerificationDiagnostic> Diagnostics,
    string AssemblyHash,
    string VerifierVersion,
    DateTimeOffset VerifiedAt);
```

## Verification stages

### V1. Hash and manifest

Check:

- DLL content hash matches manifest
- manifest plan hash matches expected plan hash
- policy hash matches current policy hash
- binding manifest hash matches current registry
- compiler version is allowed
- target sandbox/runtime version is allowed

Direct verifier use must pass an expected manifest identity through verification policy when
the caller has plan/cache context. The verifier rejects stale artifact version, cache key,
module hash, plan hash, policy hash, binding manifest hash, runtime facade hash, compiler
version, type-system version, effect-analysis version, verifier version, language version,
target framework, or optimization flags before the artifact can be treated as verified.

### V2. PE/CLR structure

Check:

- valid managed assembly
- no native entrypoint
- no mixed-mode/native code
- no suspicious sections if policy disallows
- no unmanaged exports

### V3. Assembly references

Allow only exact assembly identities required by runtime.

Example allowlist:

```text
System.Private.CoreLib
SafeIR.Runtime
```

Depending on backend, other framework assemblies may be required. Keep the list minimal.

Reject unexpected `AssemblyRef` entries.

### V4. Type references

Allow only exact approved runtime types.

Allowed example:

```text
SafeIR.SandboxContext
SafeIR.SandboxValue
SafeIR.Runtime.CompiledRuntime
System.Int32
System.Boolean
System.String
System.Void
```

Reject dangerous types:

```text
System.Type
System.Reflection.*
System.Runtime.Loader.*
System.Runtime.InteropServices.*
System.Diagnostics.Process
System.IO.*
System.Net.*
System.Threading.*
System.Threading.Tasks.*
System.Activator
System.Environment
System.GC
System.Delegate
System.Linq.Expressions.*
Microsoft.CSharp.*
```

### V5. Member references

Allow exact approved members only.

Prefer exact method signatures:

```text
SafeIR.Runtime.CompiledRuntime.ChargeFuel(SandboxContext, Int32)
SafeIR.Runtime.CompiledRuntime.CallBinding(SandboxContext, String, SandboxValue[])
SafeIR.SandboxValue.FromInt32(Int32)
SafeIR.Runtime.CompiledRuntime.AsI32(SandboxValue)
```

Reject all other method/field references.

### V6. Method definitions

Check generated methods:

- expected type/method names only
- expected public surface only
- no generic type or method parameters
- no unexpected method attributes
- no `pinvokeimpl`
- no `internalcall`
- no synchronized methods
- no unmanaged calling conventions
- no finalizers
- no static constructors unless explicitly allowed

### V7. Fields

Reject by default:

- mutable static fields
- thread-static fields
- fields of forbidden types
- pointer fields

Allow only compiler-known constants if needed.

### V8. Custom attributes

Reject unknown custom attributes.

Allowed optional attributes:

- compiler-generated marker
- debugger hidden/step-through if desired
- sandbox module hash attribute if verifier understands it

### V9. Resources

Reject embedded resources by default.

If resources are allowed later, they must be:

- size-limited
- hash-verified
- non-executable
- included in manifest

### V10. P/Invoke/native interop

Reject:

- `ImplMap` table entries
- `DllImportAttribute`
- `SuppressUnmanagedCodeSecurityAttribute`
- unmanaged calling convention metadata
- function pointer use

### V11. Method body opcode verification

Allowed opcode set should be minimal.

Typical allowed opcodes:

```text
nop
ldarg.*
starg.* optional
ldloc.*
stloc.*
ldc.i4.*
ldc.i8
ldc.r4
ldc.r8
ldstr
br
br.s
brtrue
brtrue.s
brfalse
brfalse.s
beq/bne.un/blt/bgt/etc if compiler uses them
add/sub/mul/div/rem
neg
and/or/xor/not
ceq/clt/cgt
call exact allowlist only
callvirt exact allowlist only if needed
newobj exact allowlist only
newarr only if allowed for SandboxValue[] args
ret
pop
dup optional
```

Forbidden opcodes:

```text
calli
jmp
localloc
cpblk
initblk
ldftn
ldvirtftn
ldtoken unless explicitly allowed
mkrefany
refanytype
refanyval
arglist
readonly/volatile/constrained unless verifier understands exact usage
throw/rethrow unless using approved sandbox exception flow
unbox/unbox.any unless approved
box unless approved
castclass/isinst unless approved
ldind/stind
ldfld/stfld/ldsfld/stsfld unless exact allowlist
```

### V12. Calls inside method bodies

For every `call`, `callvirt`, and `newobj` token:

- resolve token
- match exact assembly/type/member/signature allowlist
- reject otherwise

### V13. Control-flow sanity

Check:

- branches target valid instruction offsets
- no invalid exception handlers
- no unverifiable control-flow patterns
- stack height consistency if using stack IL analysis

### V14. Exception handlers

MVP: reject exception handlers in generated code.

Later: allow only compiler-known try/catch for sandbox errors if verifier is expanded.

### V15. Debug symbols

PDBs are optional.

If loaded/stored:

- do not include host paths unless sanitized
- include source maps by IR node ID
- hash PDB in manifest
- do not trust PDB for security decisions

## Verification failure handling

On failure:

- do not load/execute the assembly
- quarantine artifact
- record diagnostics
- optionally fall back to interpreter using original verified plan
- alert if failure indicates compiler bug

## Verifier implementation notes

Use metadata readers rather than reflection-load for security checks.

Suggested library:

```text
System.Reflection.Metadata
```

Do not load the assembly into the runtime before verification.

## Allowlist source

Allowlist must be generated from trusted runtime facade definitions or hard-coded reviewed descriptors.

Do not build allowlist dynamically from user input.

## Test fixtures

Verifier test suite must include DLLs with:

- `System.IO.File.ReadAllText`
- `Assembly.Load`
- `Type.GetType`
- `MethodInfo.Invoke`
- `Activator.CreateInstance`
- P/Invoke
- `calli`
- `ldtoken`
- static mutable fields
- embedded resources
- unexpected assembly refs
- direct `HttpClient`
- `Thread.Start`
- exception handlers
- mutable static constructors

Every fixture must be rejected.

## Verifier is mandatory for cached DLLs

Cached artifacts must be reverified before execution unless there is a strong signed trust model. Even then, reverify at least after software updates.

Recommended policy:

```text
Always verify on first load after process start.
Cache verification result by assembly content hash + verifier version.
```
