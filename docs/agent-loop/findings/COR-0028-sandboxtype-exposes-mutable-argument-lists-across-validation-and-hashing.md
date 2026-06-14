---
id: COR-0028
area: correctness
status: verified
priority: high
title: SandboxType exposes mutable argument lists across validation and hashing
dedup_key: correctness/public-model/sandbox-type/mutable-arguments
created_at: 2026-06-12T22:25:03.1520533+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-12T23:22:14.4943777+00:00
claimed_by: worker
claimed_at: 2026-06-12T23:13:12.0419201+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-12T23:18:08.9864168+00:00
fixed_commit: 
verified_by: codex-verifier
verified_at: 2026-06-12T23:22:14.4943777+00:00
verified_commit: 
duplicate_of: 
---

# COR-0028: SandboxType exposes mutable argument lists across validation and hashing

## Claim

`SandboxType` exposes caller-owned mutable `Arguments` through a public record property, so a type that has already been validated, hashed, embedded in a plan, or used as a dictionary/hash key can change shape after the trust boundary.

## Evidence

- `src/DotBoxd.Kernels/Sandbox/SandboxType.cs:5` declares `public sealed record SandboxType(string Name, IReadOnlyList<SandboxType> Arguments)` with no constructor body or init accessor that snapshots `Arguments`.
- `src/DotBoxd.Kernels/Sandbox/SandboxType.cs:65` through `src/DotBoxd.Kernels/Sandbox/SandboxType.cs:85` implement equality and hashing by iterating the live `Arguments` collection.
- `src/DotBoxd.Kernels/Sandbox/SandboxType.cs:116` through `src/DotBoxd.Kernels/Sandbox/SandboxType.cs:137` uses the live collection to decide whether a type is known and whether map keys are valid.
- `src/DotBoxd.Kernels.Validation/StructuralValidator.cs:47` validates function return/parameter types, and `src/DotBoxd.Kernels.Validation/StructuralValidator.cs:198` through `src/DotBoxd.Kernels.Validation/StructuralValidator.cs:203` checks map key/type validity from the same mutable `Arguments` data.
- `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:182` through `src/DotBoxd.Kernels/Model/CanonicalModuleHasher.cs:185` hashes module types by walking `type.Arguments`, while `src/DotBoxd.Kernels.Compiler/IlEmitterPrimitives.cs:55` through `src/DotBoxd.Kernels.Compiler/IlEmitterPrimitives.cs:71` emits compiled type constants from the same mutable shape.
- `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:57` through `src/DotBoxd.Kernels/Sandbox/SandboxValueValidator.cs:81` validates runtime list/map values against `expectedType.Arguments`.
- `tests/DotBoxd.Kernels.Tests/Misc06/PublicModelImmutabilityTests.cs` covers modules, plugin manifests, values, audit payloads, and validation exceptions, but it has no `SandboxType` argument-aliasing test.

A minimal repro shape is:

```csharp
var args = new List<SandboxType> { SandboxType.I32 };
var type = new SandboxType("List", args);
Assert.True(type.IsKnown());
var hash = type.GetHashCode();
args[0] = SandboxType.String;
Assert.Equal("List<String>", type.ToString());
Assert.NotEqual(hash, type.GetHashCode());
```

## Impact

`SandboxType` is core validation and identity evidence. If a host, binding descriptor, plugin adapter, or direct model construction path passes a mutable argument list, later mutation can make an already-prepared module or binding signature disagree with the canonical module hash, binding manifest hash, compiled emitted type, or runtime argument validator. Because equality/hash code also depend on the mutable list, using such a type inside hashed structures can become unstable after insertion.

This is distinct from COR-0024 and COR-0025: those findings cover validation result collections and verifier manifest/result models, while this is the core public type model used throughout validation, hashing, compilation, and runtime checks.

## Better target

Make `SandboxType` copy `Arguments` on construction and init, matching `SandboxModule`, `SandboxFunction`, `SandboxValue`, and `PluginManifest`. Add public-model immutability tests for constructor and `with { Arguments = ... }` paths, including nested list/map types.
