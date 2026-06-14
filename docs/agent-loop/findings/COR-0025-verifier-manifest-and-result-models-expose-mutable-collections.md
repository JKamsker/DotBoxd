---
id: COR-0025
area: correctness
status: fixed_pending_verification
priority: medium
title: Verifier manifest and result models expose mutable collections
dedup_key: correctness/compiler/verifier-models/mutable-manifest-verification-collections
created_at: 2026-06-12T22:19:57.5017303+00:00
created_by: correctness-producer
created_commit: 
updated_at: 2026-06-13T05:54:21.8353123+00:00
claimed_by: worker
claimed_at: 2026-06-13T05:51:08.8259078+00:00
claim_branch: workflow-work
fixed_by: worker
fixed_at: 2026-06-13T05:54:21.8353123+00:00
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# COR-0025: Verifier manifest and result models expose mutable collections

## Claim

Compiled verifier identity models expose mutable collection inputs. `ArtifactManifest.OptimizationFlags` and `VerificationResult.Diagnostics` are accepted and stored as `IReadOnlyList<T>` without defensive copies, and `CompiledArtifact` then stores those manifest/result objects directly after validation. A caller can mutate compiled-artifact identity and verifier evidence after construction by retaining or down-casting the original list.

## Evidence

`src/DotBoxd.Kernels.Verifier/Generated/VerificationModels.cs:3` declares `ArtifactManifest` as a plain record with `IReadOnlyList<string> OptimizationFlags` at `src/DotBoxd.Kernels.Verifier/Generated/VerificationModels.cs:17`. The record has no constructor body or init accessor that snapshots the flags list. `src/DotBoxd.Kernels.Verifier/Generated/VerificationModels.cs:57` similarly declares `VerificationResult` with `IReadOnlyList<VerificationDiagnostic> Diagnostics` and does not copy it.

`src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.cs:12` creates a mutable `List<VerificationDiagnostic>` and returns it directly in `new VerificationResult(...)` at `src/DotBoxd.Kernels.Verifier/Generated/GeneratedAssemblyVerifier.cs:48`, so the concrete list identity is part of the public result. `src/DotBoxd.Kernels.Compiler/CompilerContracts.cs:24` through `src/DotBoxd.Kernels.Compiler/CompilerContracts.cs:74` validates hashes and success, but then assigns `Manifest = manifest` and `Verification = verification` at `src/DotBoxd.Kernels.Compiler/CompilerContracts.cs:88` and `src/DotBoxd.Kernels.Compiler/CompilerContracts.cs:89` without copying the nested collection state.

Existing backend isolation coverage checks that `CompiledArtifact.AssemblyBytes` are defensively copied in `tests/DotBoxd.Kernels.Tests/Misc01/BackendIsolationTests.cs:104`, but there is no equivalent coverage for manifest optimization flags or verification diagnostics.

A minimal repro shape is:

```csharp
var flags = new List<string> { "boxed-values" };
var manifest = new ArtifactManifest(..., flags, assemblyHash, DateTimeOffset.UtcNow);
var verification = new VerificationResult(true, new List<VerificationDiagnostic>(), assemblyHash, verifierVersion, DateTimeOffset.UtcNow);
var artifact = new CompiledArtifact(bytes, assemblyHash, manifest, verification, entrypoint, CompiledRuntimeFormKind.LoadedAssembly);
flags.Clear();
Assert.Equal(new[] { "boxed-values" }, artifact.Manifest.OptimizationFlags); // currently fails
```

The same aliasing applies to `verification.Diagnostics`: callers can mutate verifier evidence after it has been returned or embedded in a compiled artifact.

## Risk

Compiled artifact manifests and verification results are evidence objects used by the executable guard, persistent cache validation, cache metadata, and diagnostics. If their collection payloads can change after construction, consumers can observe different optimization flags or verifier diagnostics for the same artifact depending on timing. For manifests, mutation can also turn a previously valid artifact envelope into a later validation failure or make cache/evidence reporting disagree with the state that passed the constructor gate.

## Suggested test

Add public model immutability coverage that constructs `ArtifactManifest` with a mutable `List<string>` and `VerificationResult` with a mutable `List<VerificationDiagnostic>`, mutates those lists after construction, and asserts the model properties are unchanged and not castable back to the mutable source. Add a `CompiledArtifact` regression that mutates the original manifest flags after artifact construction and asserts `artifact.Manifest.OptimizationFlags` remains stable.

## Expected behavior

Verifier and compiled-artifact evidence models should be immutable snapshots. Mutating caller-owned lists after constructing `ArtifactManifest`, `VerificationResult`, or `CompiledArtifact` must not change the manifest or verifier evidence observed by later guards, cache paths, or diagnostics.

## Suggested fix direction

Apply the same defensive-copy pattern used by core module models: copy `ArtifactManifest.OptimizationFlags`, `VerificationManifestIdentity.OptimizationFlags`, and `VerificationResult.Diagnostics` into read-only arrays/collections in their record bodies or explicit constructors. `CompiledArtifact` should either rely on those immutable model types or snapshot manifest/result inputs before storing them.

## Deduplication key

`correctness/compiler/verifier-models/mutable-manifest-verification-collections`
