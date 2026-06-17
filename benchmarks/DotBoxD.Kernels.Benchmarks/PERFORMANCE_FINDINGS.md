# DotBoxD Performance Findings

This file tracks the focused performance findings from the current perf-hunter pass.
All numbers are local Release stopwatch probes on the same machine and are intended
as targeted before/after evidence, not BenchmarkDotNet statistical reports.

## Results

| Finding | Probe | Workload | Before total | Before ns/op | Before alloc | Before B/op | After total | After ns/op | After alloc | After B/op | Notes |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Scalar `ValueShapeCache` used the generic shape walker for every append. | `--probe-value-shape-cache` | 10,000 scalar `ListAdd` calls | 12.1 ms | 1,210 | 10,099,752 B | 1,010.0 | 12.3 ms | 1,230 | 8,259,752 B | 826.0 | Same fuel and collection-element accounting; allocation reduction is the stable signal. |
| HTTP response metadata was measured once for quota checking and again for charging. | `--probe-http-metadata` | 100,000 responses with 24 headers | 615.4 ms | 6,154 | 354,700,000 B | 3,547.0 | 99.8 ms | 998 | 176,800,040 B | 1,768.0 | Same `55,300,000` charged network bytes. |
| Scalar binding returns opened string-credit scope and ran the recursive validated shape meter. | `--probe-binding-return-credit` | 500,000 I32 binding returns | 127.0 ms | 254 | 124,000,152 B | 248.0 | 101.9 ms | 204 | 68,000,040 B | 136.0 | String returns still use credit tracking; scalar returns now avoid it. |
| `BindingRegistry.TryGet` copied signatures and `Signatures` rebuilt the sorted array. | `--probe-binding-registry` | 1,000 bindings; 200,000 lookups; 5,000 signature reads | 20.6 ms / 544.2 ms | 103.0 / 108,840 | 38,400,040 B / 1,000,240,040 B | 192.0 / 200,048.0 | 5.6 ms / 0.0 ms | 28.0 / 0.0 | 40 B / 40 B | 0.0 / 0.0 | Probe now uses precomputed IDs and an in-process legacy simulation for clean before/after numbers. |
| `BindingRegistryBuilder.Build` validated descriptors, then `BindingRegistry` validated the same frozen descriptors again. | `--probe-binding-registry` | 200 builds of 1,000 bindings | 1,200.1 ms | 6,000,500 | 1,459,970,704 B | 7,299,854 | 964.1 ms | 4,820,500 | 1,446,376,080 B | 7,231,880 | Public `new BindingRegistry(...)` still validates; the builder uses an internal validated handoff after its existing validation pass. |
| Unlimited host-call accounting built quota strings and updated per-binding counts even without a per-binding limit. | `--probe-host-call-accounting` | 1,000,000 `ChargeHostCall` calls | 73.7 ms | 73.7 | 232,000,136 B | 232.0 | 2.6 ms | 2.6 | 40 B | 0.0 | Limited-control path moved from 58.8 ms / 232,000,136 B to 35.6 ms / 256 B. |
| No-op compiled binding dispatch allocated a grant-clock scope and a success-path return-validation message. | `--probe-binding-dispatch-scope` | 500,000 no-arg `Unit` binding calls | 228.4 ms | 456.8 | 87,769,944 B | 175.5 | 218.1 ms | 436.2 | 184 B | 0.0 | Struct grant-clock scope alone reduced the probe to 68,000,184 B; lazy return-validation messages removed the remaining per-call allocation. |
| Generated zero-argument runtime-stub binding calls allocated a fresh empty argument array. | `--probe-compiled-binding-arity` | 500,000 generated-shape zero-arg binding calls | 236.4 ms | 472.8 | 12,000,184 B | 24.0 | 221.7 ms | 443.4 | 184 B | 0.0 | `ChargeValueArray` still charges the same resource fuel/allocation; only the backing CLR empty array is shared. |

## Probe Commands

```powershell
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-value-shape-cache
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-http-metadata
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-binding-return-credit
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-binding-registry
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-host-call-accounting
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-binding-dispatch-scope
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-compiled-binding-arity
```
