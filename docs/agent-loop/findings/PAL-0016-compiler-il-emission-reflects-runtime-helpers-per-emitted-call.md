---
id: PAL-0016
area: perf_alloc
status: open
priority: medium
title: Compiler IL emission reflects runtime helpers per emitted call
dedup_key: alloc/compiler-il-emission/runtime-method-reflection-lookup
created_at: 2026-06-12T22:11:11.5243003+00:00
created_by: continuous-performance-producer
created_commit: 
updated_at: 2026-06-12T22:11:11.5243003+00:00
claimed_by: 
claimed_at: 
claim_branch: 
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# PAL-0016: Compiler IL emission reflects runtime helpers per emitted call

## Claim

Compiler IL emission resolves every `CompiledRuntime` helper by scanning reflection metadata at each call emission site, so compile/prepare time allocates and repeats method lookup work proportional to emitted expressions, meters, literals, and type nodes.

## Evidence

- `src/DotBoxd.Kernels.Compiler/IlEmitterPrimitives.cs:52` exposes `Runtime(string name)` for emitter call sites.
- `src/DotBoxd.Kernels.Compiler/IlEmitterPrimitives.cs:53` implements that helper as `typeof(CompiledRuntime).GetMethods(...).Single(...)`, which enumerates public static runtime methods for each lookup instead of using cached `MethodInfo` values.
- Hot emitters call this helper repeatedly: meter emission uses it for every fuel/loop/call-depth instruction at `src/DotBoxd.Kernels.Compiler/Emitters/CompiledMeterEmitter.cs:13`, `src/DotBoxd.Kernels.Compiler/Emitters/CompiledMeterEmitter.cs:20`, `src/DotBoxd.Kernels.Compiler/Emitters/CompiledMeterEmitter.cs:26`, and `src/DotBoxd.Kernels.Compiler/Emitters/CompiledMeterEmitter.cs:32`.
- Expression and collection emission also call it throughout large generated bodies, for example `src/DotBoxd.Kernels.Compiler/Emitters/MethodEmitter.cs:105`, `src/DotBoxd.Kernels.Compiler/Emitters/MethodEmitter.cs:201`, `src/DotBoxd.Kernels.Compiler/Emitters/PureBindingCallEmitter.cs:20`, and `src/DotBoxd.Kernels.Compiler/Emitters/ValueArrayEmitter.cs:17`.
- `src/DotBoxd.Kernels.Compiler/Emitters/ReflectionEmitSandboxCompiler.cs:116` emits every reachable function during `CompileAsync`, so the repeated reflection lookup sits on the cold-compile/cache-miss preparation path.
- The benchmark project has verifier, interpreter, IPC, JSON, plugin, map, HTTP, and binding-reference benchmarks, but no compiler IL emission benchmark that scales emitted statement/expression count.

## Impact

Large generated modules pay avoidable reflection enumeration and LINQ work during compilation before verification/cache publication. This does not affect runtime dispatch after PAL-0013; it is a separate preparation-time allocation path that grows with generated IL size and can make cache misses or warmup more expensive than necessary.

## Better target

Cache `MethodInfo` values once, either as static readonly fields for known runtime helpers or a static ordinal dictionary keyed by helper name. Emitters should reuse cached handles so method lookup cost is O(runtime helper count) per process rather than O(emitted helper calls * runtime helper count) per compile.

## Benchmark/allocation test idea

Add a BenchmarkDotNet compiler benchmark that builds modules with 100, 1,000, and 10,000 arithmetic statements plus loop/meter sites, then measures `ReflectionEmitSandboxCompiler.CompileAsync` time and allocated bytes on cache miss. Include a counter or allocation assertion showing runtime helper lookup does not allocate per emitted call instruction.
