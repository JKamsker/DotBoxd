# DotBoxD.Kernels Benchmark History

This file is the performance ledger for DotBoxD.Kernels interpreter/compiler optimization work.
Each optimization commit should append the benchmark command and the before/after
numbers it used.

All results below are local stopwatch probes on this machine, run in Release mode.
Ratios are relative to handwritten C# measured in the same run. These probes are
intended for regression hunting and directionally comparing implementation steps;
they are not BenchmarkDotNet statistical reports.

## Commands

```powershell
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-compiled
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-bindings
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-matrix
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-examples
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-prepared-values
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-runtime-types
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-resource-meter
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-compiled-binding-fast-path
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-value-shape-cache
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-http-metadata
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-binding-return-credit
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-binding-registry
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-host-call-accounting
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-binding-dispatch-scope
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-compiled-binding-arity
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-capability-grant-lookup
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-compiled-binding-structural-validation
dotnet run -c Release --project benchmarks/DotBoxD.Kernels.Benchmarks -p:UseSharedCompilation=false -- --probe-i32-math-intrinsic
```

## History

| Step | Commit | Probe | Key result |
| --- | --- | --- | --- |
| I32 interpreted loop fast path | `44bc06f` | `--probe-compiled` | Interpreted scalar loop dropped to about 3.3x to 3.5x handwritten in subsequent scalar probes. |
| I32 compiled raw loop path | `024f1ca` | `--probe-compiled` | Scalar compiled loop reached 62.2 ms vs 47.9 ms handwritten, or 1.3x. |
| Binding crossing optimization | `216eec6` | `--probe-bindings` | `math.sqrt` crossing improved from compiled 542.1 ms / 68.8x to 196.7 ms / 25.1x; interpreted improved from 677.7 ms / 86.0x to 514.7 ms / 65.6x. |
| Performance matrix and string length direct path | `31fa6fe` | `--probe-matrix` | Added a matrix for worse cases. `string.length` compiled improved from about 426 ms to 59-62 ms for 1M calls; interpreted improved from about 411 ms to 299-305 ms. |
| Local function call in I32 loop fast path | `fe7c6ef` | `--probe-matrix` | `local function call` improved from compiled 73.1 ms / 352.2x and interpreted 266.6 ms / 1284.3x to compiled 20.6 ms / 97.7x and interpreted 23.2 ms / 109.8x. |
| Direct binding loop adapters | `9cece3c` | `--probe-matrix` | Added direct F64 math and `string.length` loop adapters with bulk binding charges. `math.sqrt` improved from compiled 177.2 ms / 22.9x and interpreted 374.8 ms / 48.4x to compiled 23.1 ms / 3.0x and interpreted 18.2 ms / 2.4x. `string.length` improved from compiled 64.7 ms / 303.4x and interpreted 311.0 ms / 1457.4x to compiled 17.5 ms / 87.6x and interpreted 1.0 ms / 4.9x; its ratio remains distorted by the sub-millisecond handwritten baseline. |
| Direct `list.count` loop adapter | `23551ba` | `--probe-matrix` | `list.count` improved from compiled 72.9 ms / 314.8x and interpreted 196.6 ms / 848.5x to compiled 18.2 ms / 83.6x and interpreted 1.0 ms / 4.6x by bulk-charging collection read fuel and reusing the raw count in the loop. |
| Direct `list.get` I32 loop adapter | `904087c` | `--probe-matrix` | `list.get` improved from compiled 74.7 ms / 137.6x and interpreted 270.1 ms / 497.7x to compiled 24.0 ms / 45.9x and interpreted 18.2 ms / 34.7x by bulk-charging collection read fuel and emitting raw I32 index/value operations. |
| Direct `map.get` I32 loop adapter | `fe6cb0c` | `--probe-matrix` | `map.get` improved from compiled 220.4 ms / 44.4x and interpreted 170.0 ms / 34.2x to compiled 155.2 ms / 32.1x and interpreted 149.5 ms / 31.0x by bulk-charging map read fuel while preserving per-iteration key literal charging. |
| Hoisted `map.get` literal-key lookup | `99db2cb` | `--probe-matrix` | `map.get` improved from compiled 155.2 ms / 32.1x and interpreted 149.5 ms / 31.0x to compiled 98.3 ms / 20.3x and interpreted 53.7 ms / 11.1x by resolving the immutable literal-key lookup once and still charging the key literal in the loop. |
| Bulk `map.get` key literal charging | `87765f0` | `--probe-matrix` | `map.get` improved from compiled 98.3 ms / 20.3x and interpreted 53.7 ms / 11.1x to compiled 19.7 ms / 4.1x and interpreted 0.5 ms / 0.1x by bulk-charging the literal key value and reusing the hoisted key/result in the loop. |
| Direct `list.get` I32 reader | `aa15dd2` | `--probe-matrix` | `list.get` improved from compiled 25.0 ms / 47.7x and interpreted 18.2 ms / 34.8x to compiled 19.3 ms / 36.6x and interpreted 11.0 ms / 20.9x by building an I32 reader once and reusing raw items in the loop. |
| Direct `list.get` modulo index shortcut | `a514d91` | `--probe-matrix` | `list.get` interpreted improved from 11.0 ms / 20.9x to 1.7 ms / 3.3x by recognizing raw variable remainder indexes such as `i % 3`; compiled stayed about flat at 19.7 ms / 37.4x. |
| Compiled `list.get` cyclic accumulator | `d134853` | `--probe-matrix` | Same-machine baseline from `a514d91` measured compiled `list.get` at 19.4 ms / 36.5x. This step measured 18.2 ms / 34.0x by replacing the zero-based `total += items[i % constant]` emitted loop with a verifier-allowlisted bulk helper. |
| Nested F64 binding crossings | this commit | `--probe-matrix` | Added `math.sqrt x3 binding`, which calls `math.sqrt` three times per loop iteration. Same-machine baseline from `d134853` measured interpreted at 472.1 ms / 40.5x and compiled at 28.8 ms / 2.5x. This step measured interpreted at 20.3 ms / 1.8x and compiled at 27.5 ms / 2.4x while charging all 3 binding calls per iteration. |
| Example workflow dispatch probe and plugin hot-path trims | this commit | `--probe-examples` | Added steady-state example coverage for a native hook chain versus a sandboxed JSON plugin. The original setup-inclusive probe exposed a large gap (`mixed fire/ice` compiled 3896.1 ms / 4954.3x, interpreted 255.2 ms / 324.5x). After separating setup from dispatch and trimming successful run summaries, empty audit snapshots, revocation checks, and default reflection compiled-cache hits, the dispatch-only probe measured `mixed fire/ice` native hook 9.6 ms, compiled 637.0 ms, interpreted 507.7 ms; `predicate miss` native hook 4.3 ms, compiled 129.3 ms, interpreted 170.8 ms; `predicate hit` native hook 3.2 ms, compiled 281.4 ms, interpreted 261.0 ms. This step improves diagnosability and removes avoidable overhead, but leaves plugin workflow dispatch far above near-native speed. |
| Event writer and live-state allocation trims | this commit | `--probe-examples` | Exposed the existing no-intermediate-list event writer path as `IPluginEventValueWriter<TEvent>` for handwritten adapters, with validation that `EventValueCount` matches `Parameters.Count`. Also cached live-setting `SandboxValue` conversions, stored execution observations as structs until snapshot, and avoided allocating a deferred live-update list when no `AsyncSet` update is pending. Current probe measured `mixed fire/ice` native hook 10.2 ms, compiled 640.8 ms, interpreted 511.8 ms; `predicate miss` native hook 6.5 ms, compiled 128.2 ms, interpreted 169.3 ms; `predicate hit` native hook 3.3 ms, compiled 269.1 ms, interpreted 257.2 ms. Stopwatch movement is noisy and does not close the dispatch gap; this step is justified as allocation trimming and public adapter access to an already-used runtime fast path. |
| Default hook context reuse | this commit | `--probe-examples` | Reused an immutable default `HookContext` for publishes without a cancellable caller token, while preserving fresh contexts for cancellable publishes. This removes one allocation from the common hook dispatch path used by native hooks and plugin kernels. Current probe measured `mixed fire/ice` native hook 9.3 ms, compiled 532.9 ms, interpreted 648.2 ms; `predicate miss` native hook 6.5 ms, compiled 111.0 ms, interpreted 241.3 ms; `predicate hit` native hook 3.1 ms, compiled 229.2 ms, interpreted 391.9 ms. Results remain noisy, but the miss-heavy compiled path moved down from the prior sample and the workflow still remains far above near-native speed. |
| Lazy audit sink event storage | this commit | `--probe-examples` | Created the in-memory audit event list only when an event is written, so successful plugin entrypoints that suppress the run summary and emit no binding/cache audit do not pay for an empty per-run `List<SandboxAuditEvent>`. Current probe measured `mixed fire/ice` native hook 10.3 ms, compiled 579.5 ms, interpreted 576.3 ms; `predicate miss` native hook 7.1 ms, compiled 119.1 ms, interpreted 222.8 ms; `predicate hit` native hook 3.2 ms, compiled 229.2 ms, interpreted 517.7 ms. The miss-heavy compiled path remains in the same band as the prior sample, while the change is directly covered by an allocation regression test for empty sinks. |
| Compiled no-audit success path | this commit | `--probe-examples` | Used a narrow compiled fast path for entrypoints with no binding references when successful run summaries are suppressed and no cache-invalidated audit must be emitted. Failures still produce failed `RunSummary` audit, and binding entrypoints still preserve binding audit events. Current probe measured `mixed fire/ice` native hook 17.9 ms, compiled 655.2 ms, interpreted 619.0 ms; `predicate miss` native hook 1.6 ms, compiled 83.3 ms, interpreted 253.4 ms; `predicate hit` native hook 3.5 ms, compiled 251.8 ms, interpreted 782.4 ms. The miss-only compiled path benefits most because it is just `ShouldHandle`; hit and mixed cases still include the audited `Handle` binding path. |
| Installed-kernel prepared host dispatch | this commit | `--probe-examples` | Routed installed kernels through an internal in-process host execution path that still enforces disposal, capability revocation, deterministic policy, runtime mode selection, fallback, and audit observer publication, but skips the repeated public prepared-plan integrity guard for plans produced during plugin installation. Current probe measured `mixed fire/ice` native hook 10.7 ms, compiled 528.9 ms, interpreted 510.2 ms; `predicate miss` native hook 1.5 ms, compiled 66.6 ms, interpreted 202.3 ms; `predicate hit` native hook 3.1 ms, compiled 218.7 ms, interpreted 478.3 ms. The example workflow remains far above native hook dispatch, but this removes another fixed per-entrypoint host envelope cost. |
| Plugin message binding clean-payload trim | this commit | `--probe-examples` | Avoided copying clean plugin message payload strings during sink sanitization and built plugin-message audit fields in one mutable dictionary instead of cloning the base binding-audit field dictionary to add `messageLength`. Current probe measured `mixed fire/ice` native hook 9.6 ms, compiled 495.5 ms, interpreted 461.8 ms; `predicate miss` native hook 1.5 ms, compiled 67.7 ms, interpreted 187.7 ms; `predicate hit` native hook 2.9 ms, compiled 206.5 ms, interpreted 441.8 ms. This primarily affects hit/mixed cases that execute the audited `host.message.send` binding; miss-only dispatch remains dominated by `ShouldHandle`. |
| Synchronous hook and message dispatch fast paths | this commit | `--probe-examples` | Kept hook publish and `host.message.send` binding dispatch on completed `ValueTask` fast paths, falling back to awaited helpers only when a filter, handler, or sink actually suspends. Three local samples measured `mixed fire/ice` native hook 7.1-7.2 ms, compiled 480.9-518.2 ms, interpreted 489.9-536.9 ms; `predicate miss` native hook 1.2-1.3 ms, compiled 64.5-70.9 ms, interpreted 177.9-190.6 ms; `predicate hit` native hook 2.3-2.4 ms, compiled 193.9-213.4 ms, interpreted 400.4-486.4 ms. Compared with the previous row, native hook dispatch moved down consistently; compiled/interpreted plugin dispatch remains noisy and still far from native. |
| Compiled runtime scalar type singleton reuse | this commit | `--probe-runtime-types` | `CompiledRuntime.TypeScalar("I32")` now returns the built-in scalar singleton used by generated entrypoint type checks instead of rebuilding `SandboxType.Scalar("I32")`. Two local samples for 2M calls measured the allocating scalar baseline at 115.8-123.9 ms and 112,000,040 B, while the compiled-runtime built-in path measured 21.5-25.7 ms and 40 B. The non-built-in fallback stayed allocating as expected: `CompiledRuntime.TypeScalar("MonsterId")` measured 105.6-115.4 ms and 112,000,040 B. |
| Built-in scalar validation fast path | this commit | `--probe-runtime-types`, `--probe-examples` | Short-circuited `SandboxValueValidator.RequireType` when the value is a built-in scalar and the expected type is the matching singleton, preserving the existing generic path for non-singleton and opaque-id scalar types. Two local runtime-type samples for 2M calls measured forced generic validation with `RequireType(I32, Scalar("I32"))` at 313.4-319.4 ms and 40 B, while the singleton fast path `RequireType(I32, SandboxType.I32)` measured 19.7-27.5 ms and 40 B. One example workflow sanity sample was still noisy (`mixed fire/ice` compiled 400.2 ms, `predicate miss` compiled 160.6 ms, `predicate hit` compiled 222.9 ms), so this step claims only the direct scalar validation improvement. |
| Flat scalar value metering fast path | this commit | `--probe-resource-meter`, `--probe-examples` | Added a direct `ResourceMeter.ChargeValue` path for scalar values and small flat scalar lists, matching the generic shape-walker's resource usage while leaving larger lists on the existing fuel-charged scanner. The plugin-shaped flat input probe for 1M charges measured the generic walker baseline at 248.0 ms and 448,000,040 B with the fast path temporarily disabled; with the fast path enabled it measured 204.7-205.0 ms and 40 B, with identical `collectionElements=5,000,000` and `stringBytes=32,000,000`. One example workflow sanity sample measured `mixed fire/ice` compiled 373.4 ms, `predicate miss` compiled 83.4 ms, and `predicate hit` compiled 182.3 ms; the direct resource-meter probe is the primary evidence because the workflow baselines remain noisy. |
| Compiled prepared no-audit value result | this commit | `--probe-prepared-values`, `--probe-examples` | Routed installed-kernel compiled entrypoints with no binding references and suppressed successful audit through an internal prepared-value result, avoiding public `SandboxExecutionResult`, resource-usage snapshot, and audit-list construction on successful no-audit runs while preserving the full result path for failures and audited entrypoints. The focused compiled `ShouldHandle` miss probe for 200k calls measured the full-result path at 527.7 ms and 276,043,008 B with the new branch temporarily disabled; enabled samples measured 388.3-428.9 ms and 227,155,008-230,065,792 B. One full workflow sanity sample measured `mixed fire/ice` compiled 376.6 ms, `predicate miss` compiled 79.2 ms, and `predicate hit` compiled 233.7 ms; the focused prepared-value probe is the primary evidence. |
| Lazy binding return credit tracker | this commit | `--probe-prepared-values` | Made `SandboxContext` allocate binding return-credit tracking only when a binding return scope or credited string construction is actually used. The compiled no-audit `ShouldHandle` miss path does neither. Same-session focused probe for 200k calls measured the eager tracker at 497.4 ms and 231,727,360 B with the lazy field temporarily reverted; restored lazy samples measured 512.0-629.0 ms and 220,290,048-221,238,976 B. This step claims the allocation reduction only because stopwatch movement was noisy. |
| Bool value singleton factory | this commit | `--probe-prepared-values` | Reused immutable `BoolValue` instances from `SandboxValue.FromBool` instead of allocating a new record for every boolean result. Same-session focused compiled no-audit miss probe for 200k calls measured the allocating factory at 471.8 ms and 217,145,920 B with `FromBool` temporarily reverted; restored singleton samples measured 498.2-567.1 ms and 214,997,888-215,700,416 B. This step claims only the small allocation reduction because elapsed time was noisy. |
| Owned list snapshot wrapper trim | this commit | `--probe-prepared-values` | Let the internal owned-array list/record snapshot marker wrap the fresh array directly instead of wrapping a `ReadOnlyCollection` inside a second marker object. Public `FromList`/`FromRecord` defensive-copy behavior is unchanged. Same-session compiled no-audit miss probe for 200k calls measured the old double-wrapper path at 518.8 ms and 215,557,056 B with the change temporarily reverted; restored optimized samples measured 472.6-596.9 ms and 208,149,680-212,267,904 B. This step claims allocation reduction only because elapsed time was noisy. |
| Common I32 value factory cache | this commit | `--probe-prepared-values` | Reused immutable `I32Value` instances for common values `-1..256` from `SandboxValue.FromInt32`, covering loop counters, small counts, and the example event amount without broadening the public API. Same-session compiled no-audit miss probe for 200k calls measured the allocating factory at 495.0 ms and 211,228,736 B with `FromInt32` temporarily reverted; restored cache samples measured 414.6-635.0 ms and 203,405,312-204,579,904 B. This step claims allocation reduction only because elapsed time was noisy. |
| Installed no-audit resource meter reuse | this commit | `--probe-prepared-values` | Reused a reset `ResourceMeter` owned by the serialized installed-kernel path for compiled no-binding entrypoints, while public host execution and audited/binding entrypoints keep their existing per-run meters. Same-session compiled no-audit miss probe for 200k calls measured the non-reuse path at 487.8 ms and 206,049,728 B with reusable meter selection temporarily disabled; restored reuse samples measured 471.6-508.3 ms and 177,604,288-181,009,024 B. This step claims the allocation reduction and notes elapsed time as directionally positive but still stopwatch-noisy. |
| List value self-view for owned arrays | this commit | `--probe-prepared-values` | Stored `ListValue` snapshots in a private array and exposed the list value itself as the read-only view, removing the separate owned-snapshot wrapper object from multi-parameter plugin inputs while keeping public `FromList` defensive-copy behavior. Same-session compiled no-audit miss probe for 200k calls measured the old owned-snapshot path at 407.0 ms and 179,228,800 B with the self-view temporarily disabled; restored self-view samples measured 478.6-484.0 ms and 176,143,744-177,293,120 B. This step claims allocation reduction only because elapsed time was noisy. |
| Installed no-audit sandbox context reuse | this commit | `--probe-prepared-values` | Reused a reset `SandboxContext` alongside the reusable no-audit `ResourceMeter` for serialized installed-kernel compiled entrypoints with no binding references, while fresh contexts remain in use when the effective cancellation token changes. Same-session compiled no-audit miss probe for 200k calls measured the fresh-context path at 467.3 ms and 172,997,632 B with context reuse temporarily disabled but meter reuse intact; restored context-reuse samples measured 475.9-508.2 ms and 151,964,288-152,961,408 B. This step claims allocation reduction only because elapsed time was noisy. |
| Compiled cache composite keys | this commit | `--probe-prepared-values` | Replaced per-lookup `planHash + "|" + entrypoint` string keys in the reflection artifact and executable hit caches with small composite struct keys, preserving the same LRU/cache behavior without allocating two concatenated strings per compiled dispatch. The immediately preceding same-session string-key sample measured 475.9 ms and 152,961,408 B for 200k compiled no-audit misses; composite-key samples measured 441.4-462.4 ms and 78,238,464-79,247,936 B. |
| Installed no-audit executable shortcut | this commit | `--probe-prepared-values` | Cached the compiled executable in the installed-kernel no-audit run state after the first verified no-audit dispatch, with a single-entry fast path for the common one-entrypoint case and the existing host compiled caches still used for first lookup and non-installed execution. Same-session compiled no-audit miss probe for 200k calls measured the provider-cache path at 487.1 ms and 78,646,064 B with the shortcut temporarily disabled; restored shortcut samples measured 389.6-451.5 ms and 37,084,800-38,764,032 B. |
| Installed no-audit input buffer reuse | this commit | `--probe-prepared-values`, `--probe-examples` | Reused the synthetic multi-parameter input array for installed-kernel compiled `ShouldHandle` entrypoints with no binding references, while snapshotting the input before any non-no-audit `Handle` dispatch so audited/binding paths keep immutable inputs. Same-session compiled no-audit miss probe for 200k calls measured the fresh-input path at 434.7 ms and 41,600,064 B with buffer reuse disabled; restored reuse samples measured 418.7-447.9 ms and 19,595,136-24,159,296 B. One workflow sample measured compiled `predicate miss` at 56.6 ms, down from the prior 74.3-80.6 ms band, while hit/mixed cases remained noisy. |
| Installed no-audit input list reuse | this commit | `--probe-prepared-values` | Reused the synthetic `ListValue` wrapper together with the no-audit input buffer for installed-kernel compiled `ShouldHandle` entrypoints, keeping the public defensive-copy list path unchanged and still snapshotting before non-no-audit `Handle` dispatch. Same-session compiled no-audit miss probe for 200k calls measured the buffer-only path at 433.6 ms and 26,353,408 B with list reuse disabled; restored list-reuse samples measured 431.3-455.3 ms and 16,595,840-17,082,992 B. This step claims allocation reduction only because elapsed time was noisy. |
| Compiled two-argument binding fast path | this commit | `--probe-compiled-binding-fast-path`, `--probe-examples` | Emitted `CompiledRuntime.CallBinding2` for two-argument runtime-stub bindings and let descriptor targets that implement the internal fast invoker receive the two values without materializing a `SandboxValue[]`, while `ChargeValueArray` preserves generated-code fuel/allocation accounting. The focused real `host.message.send` probe for 200k calls measured the old array-backed shape at 373.6-424.8 ms and 334,401,112 B; the new fast path measured 135.4-140.2 ms and 322,238,456-322,842,136 B, saving 57.8-60.8 B/call after warmup. Broad workflow samples stayed noisy but sanity-ran with compiled `mixed fire/ice` at 315.0-342.1 ms and `predicate hit` at 222.9-265.3 ms. |
| Direct scalar shape-cache measurement | this commit | `--probe-value-shape-cache` | Avoided sending scalar/text values through the generic `SandboxValueShapeMeter.MeasureWithNodes` walker when composing incremental `list.add` / `map.set` shapes. The compiled scalar `ListAdd` probe for 10k appends measured the pre-change path at 12.1 ms and 10,099,752 B. After the direct scalar path, samples measured 10.7-13.4 ms and 8,259,752 B with identical `fuel=50,801,726` and `collectionElements=50,005,000`; this step claims the allocation reduction. |
| Single-pass HTTP response metadata accounting | this commit | `--probe-http-metadata` | Reused the `ChargeMetadata` return value instead of measuring response metadata once for local bookkeeping and again while charging network bytes. The in-process probe for 100k metadata charges with 24 headers measured the legacy double-measure pattern at 615.4-639.8 ms and 354,692,976-354,864,936 B; the single-pass path measured 98.6-99.8 ms and 176,800,040 B with identical `55,300,000` charged network bytes. |
| Scalar binding-return fast paths | this commit | `--probe-binding-return-credit` | Opened a binding return-credit scope only for `String` return types and measured scalar binding returns directly in `SandboxValidatedValueShapeMeter`, preserving string return double-charge prevention and scalar invariant checks. Before the scalar-shape fast path, the direct scalar-return probe for 500k charges measured the legacy always-scope path at 124.9-138.4 ms and 232,000,152 B; the conditional scalar path measured 151.7-155.4 ms and 176,000,040 B. After scalar direct validation, the same probe measured legacy I32 at 82.3-127.0 ms and 124,000,152 B, and conditional I32 at 76.7-101.9 ms and 68,000,040 B. The `String` control kept scope allocation and charged `4,000,000` string bytes. |
| Cached binding registry signatures | this commit | `--probe-binding-registry` | Cached sorted binding signatures and an ID-to-signature map at `BindingRegistry` construction instead of copying parameter arrays on every `TryGet` and rebuilding/sorting signatures on every property access. With 1,000 bindings and precomputed lookup IDs, an in-process legacy `GetDescriptor(id).Signature` simulation for 200k successful lookups measured 20.6 ms and 38,400,040 B; cached `TryGet` measured 5.6 ms and 40 B. The simulated legacy `Signatures` rebuild for 5k reads measured 544.2 ms and 1,000,240,040 B; cached `Signatures` measured 0.0 ms and 40 B. |
| Single-pass registry-builder validation | this commit | `--probe-binding-registry` | `BindingRegistryBuilder.Build` now hands already validated descriptors to an internal registry constructor path, while public `new BindingRegistry(...)` keeps its validation pass. The 200-build lane over 1,000 bindings improved from 1,200.1 ms and 1,459,970,704 B to 964.1 ms and 1,446,376,080 B. Existing builder and public-constructor validation tests cover the two externally visible validation paths. |
| Lazy unlimited host-call accounting | this commit | `--probe-host-call-accounting` | Avoided constructing interpolated quota messages on successful host-call charges, and skipped per-binding call dictionaries when a descriptor has no `MaxCallsPerRun`. The 1M-call unlimited path improved from 73.7 ms and 232,000,136 B to 2.6 ms and 40 B. The limited control path, which still tracks per-binding counts, improved from 58.8 ms and 232,000,136 B to 35.6 ms and 256 B by removing successful-path quota-string allocation. |
| Allocation-free no-op compiled binding dispatch | this commit | `--probe-binding-dispatch-scope` | Converted the binding grant-clock scope from an allocated `IDisposable` class to a concrete struct and made binding-return validation messages lazy for the success path. The 500k-call no-arg `Unit` binding probe improved from 228.4 ms and 87,769,944 B to 218.1 ms and 184 B. The intermediate struct-scope-only sample measured 222.8 ms and 68,000,184 B, isolating the remaining allocation to the eager return-validation message. |
| Shared generated zero-arg binding arrays | this commit | `--probe-compiled-binding-arity` | Reused `Array.Empty<SandboxValue>()` for generated-code `CreateLiteralValueArray(0)` calls. The generated-shape zero-argument runtime-stub binding probe improved from 236.4 ms and 12,000,184 B to 221.7 ms and 184 B for 500k calls, while `ChargeValueArray` kept the same sandbox fuel/allocation charges. |
| Capability grant lookup cache | this commit | `--probe-capability-grant-lookup` | Cached the last successful `SandboxContext.GetCapability` grant by requested capability id and `EffectiveGrantClock`, avoiding the common capability-backed binding sequence that calls `RequireCapability` and then resolves the same grant again. The 1M-pair probe improved from 24.5 ms (24.5 ns/op) and 728 B to 2.2 ms (2.2 ns/op) and 728 B; this is a time-only improvement with expiry/clock semantics preserved. |
| Structural compiled binding validation | this commit | `--probe-compiled-binding-structural-validation` | Replaced the compiled binding dispatcher's structural `.Type.Equals(expected)` argument check with a direct shape matcher keyed by scalar kind and list/map/record metadata, preserving mismatch errors while avoiding nested `SandboxType` materialization. The 1M list + record argument-pair probe (2M validations) improved from 350.2 ms, 175.1 ns/check, and 520,000,040 B to 74.8 ms, 37.4 ns/check, and 40 B. |
| Raw I32 math intrinsic helpers | this commit | `--probe-i32-math-intrinsic` | Added verifier-allowlisted raw helpers for `math.abs`, `math.min`, `math.max`, and `math.clamp`, and let the straight I32 loop fast path use them for approved pure math bindings while emitting the same `ChargeBindingCall` before each raw helper call. The charged `math.abs` probe improved from 7.5 ms and 11,643,616 B for the boxed direct helper shape to 3.9 ms and 40 B for 1M calls, with identical host-call count and total. |

Versioning note for the two-argument binding fast path: `CallBinding2` and `ChargeValueArray`
are public generated-code ABI on `CompiledRuntime` for the same reason as the existing facade
members: compiled assemblies must call them across assembly boundaries and the verifier allowlist
hashes their exact signatures. They are not supported host API.
| Compiled side-effecting runtime-stub bindings | this commit | `--probe-examples` | Allowed verified compiled entrypoints to call descriptor-governed runtime-stub bindings such as `host.message.send` through `CompiledRuntime.CallBinding`, while keeping direct runtime methods limited to pure intrinsics. This removes the compiled `Handle` fallback in the example workflow (`Handle:Compiled/fallback=none` instead of interpreted fallback). Current probe measured `mixed fire/ice` native hook 11.8 ms, compiled 638.2 ms, interpreted 607.6 ms; `predicate miss` native hook 9.6 ms, compiled 162.1 ms, interpreted 235.8 ms; `predicate hit` native hook 3.8 ms, compiled 225.5 ms, interpreted 541.7 ms. Stopwatch movement remains noisy, but the mode summary proves the compiled fallback is removed; the workflow path is still far from near-native dispatch. |

## Matrix After `31fa6fe`

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.7 ms     39.1 ms   1.7      102.7 ms    4.3
math.sqrt binding                 7.8 ms    194.8 ms  25.0      365.0 ms   46.9
string.length binding             0.2 ms     62.4 ms 288.8      305.3 ms 1413.2
list.count intrinsic              0.2 ms     47.8 ms 205.9      244.9 ms 1055.0
list.get intrinsic                0.5 ms     49.7 ms  93.5      310.8 ms  584.8
map.get intrinsic                 2.3 ms    145.2 ms  62.0      195.5 ms   83.4
local function call               0.2 ms     73.1 ms 352.2      266.6 ms 1284.3
```

## Matrix After Local Function Call Fast Path

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.4 ms     39.5 ms   1.7      104.9 ms    4.5
math.sqrt binding                 7.9 ms    209.5 ms  26.6      362.1 ms   45.9
string.length binding             0.2 ms     63.7 ms 293.5      299.6 ms 1380.5
list.count intrinsic              0.2 ms     47.4 ms 213.9      240.8 ms 1086.0
list.get intrinsic                0.5 ms     51.1 ms  95.6      308.0 ms  576.3
map.get intrinsic                 2.4 ms    134.5 ms  57.0      221.7 ms   94.0
local function call               0.2 ms     20.6 ms  97.7       23.2 ms  109.8
```

## Matrix After Direct Binding Loop Adapters

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.0 ms     39.5 ms   1.7      103.4 ms    4.5
math.sqrt binding                 7.7 ms     23.1 ms   3.0       18.2 ms    2.4
string.length binding             0.2 ms     17.5 ms  87.6        1.0 ms    4.9
list.count intrinsic              0.2 ms     72.9 ms 314.8      196.6 ms  848.5
list.get intrinsic                0.5 ms     52.4 ms  98.4      206.8 ms  388.4
map.get intrinsic                 2.4 ms    180.3 ms  76.5      270.2 ms  114.7
local function call               0.2 ms     20.9 ms 103.7       23.3 ms  115.6
```

## Matrix After Direct List Count Loop Adapter

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.3 ms     39.2 ms   1.7      106.6 ms    4.6
math.sqrt binding                 7.8 ms     23.2 ms   3.0       18.4 ms    2.4
string.length binding             0.2 ms     18.7 ms  92.5        1.0 ms    4.9
list.count intrinsic              0.2 ms     18.2 ms  83.6        1.0 ms    4.6
list.get intrinsic                0.5 ms     74.7 ms 137.6      270.1 ms  497.7
map.get intrinsic                 2.2 ms    163.8 ms  73.2      295.4 ms  132.0
local function call               0.2 ms     23.6 ms 112.5       23.1 ms  110.1
```

## Matrix After Direct List Get I32 Loop Adapter

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.0 ms     38.4 ms   1.7      105.0 ms    4.6
math.sqrt binding                 7.8 ms     23.9 ms   3.1       18.4 ms    2.4
string.length binding             0.2 ms     17.6 ms  87.1        1.0 ms    4.8
list.count intrinsic              0.2 ms     17.0 ms  79.0        1.0 ms    4.4
list.get intrinsic                0.5 ms     24.0 ms  45.9       18.2 ms   34.7
map.get intrinsic                 5.0 ms    220.4 ms  44.4      170.0 ms   34.2
local function call               0.2 ms     20.9 ms 103.6       23.2 ms  115.0
```

## Matrix After Direct Map Get I32 Loop Adapter

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.8 ms     38.5 ms   1.6      101.4 ms    4.3
math.sqrt binding                 7.8 ms     23.9 ms   3.1       18.3 ms    2.3
string.length binding             0.2 ms     17.8 ms  86.0        1.0 ms    4.7
list.count intrinsic              0.2 ms     17.7 ms  81.5        1.0 ms    4.8
list.get intrinsic                0.5 ms     25.2 ms  46.6       18.3 ms   33.9
map.get intrinsic                 4.8 ms    155.2 ms  32.1      149.5 ms   31.0
local function call               0.2 ms     21.8 ms 107.7       24.0 ms  118.6
```

## Matrix After Hoisted Map Get Literal-Key Lookup

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 22.9 ms     41.9 ms   1.8      102.5 ms    4.5
math.sqrt binding                 7.7 ms     23.0 ms   3.0       18.1 ms    2.4
string.length binding             0.2 ms     18.9 ms  93.9        1.0 ms    4.8
list.count intrinsic              0.2 ms     19.2 ms  89.1        1.0 ms    4.7
list.get intrinsic                0.5 ms     24.6 ms  47.5       19.0 ms   36.8
map.get intrinsic                 4.8 ms     98.3 ms  20.3       53.7 ms   11.1
local function call               0.2 ms     22.1 ms 106.7       24.1 ms  116.2
```

## Matrix After Bulk Map Get Key Literal Charging

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.1 ms     39.4 ms   1.7      103.2 ms    4.5
math.sqrt binding                 7.7 ms     26.0 ms   3.4       18.2 ms    2.4
string.length binding             0.2 ms     16.1 ms  80.5        0.9 ms    4.7
list.count intrinsic              0.2 ms     16.5 ms  77.9        0.9 ms    4.4
list.get intrinsic                0.5 ms     25.0 ms  47.7       18.2 ms   34.8
map.get intrinsic                 4.8 ms     19.7 ms   4.1        0.5 ms    0.1
local function call               0.2 ms     22.0 ms 109.2       23.0 ms  113.8
```

## Matrix After Direct List Get I32 Reader

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.4 ms     38.0 ms   1.6      106.4 ms    4.6
math.sqrt binding                 7.6 ms     23.2 ms   3.0       18.9 ms    2.5
string.length binding             0.2 ms     15.8 ms  79.1        0.9 ms    4.8
list.count intrinsic              0.2 ms     18.4 ms  87.1        1.0 ms    4.7
list.get intrinsic                0.5 ms     19.3 ms  36.6       11.0 ms   20.9
map.get intrinsic                 4.8 ms     18.3 ms   3.8        0.5 ms    0.1
local function call               0.2 ms     21.2 ms 104.9       23.1 ms  114.6
```

## Matrix After Direct List Get Modulo Index Shortcut

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.1 ms     39.8 ms   1.7      102.7 ms    4.4
math.sqrt binding                 7.7 ms     24.2 ms   3.1       18.4 ms    2.4
string.length binding             0.2 ms     17.3 ms  85.9        1.0 ms    4.9
list.count intrinsic              0.2 ms     17.5 ms  80.9        1.0 ms    4.5
list.get intrinsic                0.5 ms     19.7 ms  37.4        1.7 ms    3.3
map.get intrinsic                 4.9 ms     20.3 ms   4.2        0.6 ms    0.1
local function call               0.2 ms     22.3 ms 109.9       23.0 ms  113.4
```

## Matrix After Compiled List Get Cyclic Accumulator

Baseline from a temporary worktree at `a514d91`:

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 24.0 ms     41.3 ms   1.7      112.0 ms    4.7
math.sqrt binding                 7.8 ms     25.3 ms   3.2       18.8 ms    2.4
string.length binding             0.2 ms     17.3 ms  84.9        1.0 ms    4.8
list.count intrinsic              0.2 ms     22.1 ms  99.3        1.0 ms    4.4
list.get intrinsic                0.5 ms     19.4 ms  36.5        1.8 ms    3.4
map.get intrinsic                 5.1 ms     21.1 ms   4.1        0.6 ms    0.1
local function call               0.2 ms     22.6 ms 108.0       24.6 ms  117.6
```

After this change:

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.5 ms     39.6 ms   1.7      122.6 ms    5.2
math.sqrt binding                 7.8 ms     23.9 ms   3.1       18.5 ms    2.4
string.length binding             0.2 ms     17.5 ms  83.7        1.0 ms    4.8
list.count intrinsic              0.2 ms     17.5 ms  82.0        1.0 ms    4.5
list.get intrinsic                0.5 ms     18.2 ms  34.0        1.7 ms    3.2
map.get intrinsic                 5.0 ms     19.2 ms   3.9        0.5 ms    0.1
local function call               0.2 ms     20.8 ms 101.3       24.3 ms  118.5
```

## Matrix After Nested F64 Binding Crossings

Baseline from a temporary worktree at `d134853` with the new benchmark row
applied to benchmark code only:

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.2 ms     38.9 ms   1.7      103.8 ms    4.5
math.sqrt binding                 7.7 ms     23.4 ms   3.1       18.3 ms    2.4
math.sqrt x3 binding             11.7 ms     28.8 ms   2.5      472.1 ms   40.5
string.length binding             0.2 ms     18.4 ms  91.5        1.0 ms    4.9
list.count intrinsic              0.2 ms     17.3 ms  81.5        1.1 ms    5.2
list.get intrinsic                0.5 ms     17.9 ms  33.7        1.7 ms    3.3
map.get intrinsic                 2.2 ms     19.0 ms   8.5        0.5 ms    0.2
local function call               0.2 ms     21.6 ms 107.7       23.7 ms  118.2
```

After this change:

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.1 ms     39.2 ms   1.7      103.6 ms    4.5
math.sqrt binding                 7.7 ms     22.9 ms   3.0       18.2 ms    2.4
math.sqrt x3 binding             11.6 ms     27.5 ms   2.4       20.3 ms    1.8
string.length binding             0.2 ms     16.7 ms  83.5        1.0 ms    5.0
list.count intrinsic              0.2 ms     17.6 ms  79.0        1.0 ms    4.3
list.get intrinsic                0.5 ms     16.3 ms  29.7        1.7 ms    3.2
map.get intrinsic                 4.8 ms     18.9 ms   3.9        0.6 ms    0.1
local function call               0.2 ms     20.1 ms 100.0       23.0 ms  114.5
```

## Collection-build rogue fix (`--probe-rogue`)

The micro-matrix above runs with `Fuel = long.MaxValue`. Under a realistic fuel cap, per-iteration metering
already bounds wall-time — the verifier requires a `ChargeLoopIteration` on every loop back-edge, so the
compiled per-iteration metering "floor" is an intentional CPU-bound guarantee, not a removable cost. The
genuine "rogue invocation" risk is algorithmic blow-up the fuel cap does not catch tightly.

`--probe-rogue` builds a collection with repeated `list.add` / `map.set` at growing sizes (all quotas
relaxed, so wall-time scaling is visible). It exposed an O(n^2) blow-up: each add/set had three independent
O(n) costs — (a) re-walking the whole collection to measure its shape for `ChargeValue`, (b) copying the
whole backing store, and (c) deep-re-validating every element of the source via `AsList`/`AsMap`.

Fix (charged fuel/shape are byte-identical to before — verified by the full 1591-test suite incl.
differential/golden/fuel-accounting):

- Incremental shape charging: compose the result shape and scan-fuel (`nodes / 64`) in O(1) from the
  source's memoized shape instead of re-walking (`ValueShapeCache`, `SandboxValueShapeMeter.MeasureWithNodes`,
  `SandboxContext.ChargeComposedValue`).
- Structural sharing: back `list.add` with `ImmutableList` and `map.set` with `ImmutableDictionary`
  (O(log n) share) instead of copying the whole store (`ListValue.Append`, `MapValue.SetEntry`).
- Trust the already-validated, immutable source on add/set (use the read-path accessors), validating only the
  newly added element; the deep source re-walk was redundant given trust-boundary validation + immutability.

```text
build         before (compiled)   after (compiled)   speedup
list.add 16k       6,665 ms             46 ms          ~145x
list.add 64k     152,010 ms             74 ms         ~2000x
map.set  16k      14,608 ms             57 ms          ~250x
```

Scaling went from ~4x per size-doubling (quadratic) to ~1-2x (near-linear); sub-100 ms even at 64k elements.
The micro-matrix is unchanged (no regression to `list.get` / `map.get` from the immutable backings).

## Compiled per-execution floor was re-emit, not metering (in-memory artifact cache)

Earlier notes blamed the ~17 ms compiled floor on the per-iteration metering call. That was **wrong** — a
strided-metering experiment removed the per-iteration `ChargeLoopIteration` and the `i32` loop did not move
at all. A `trivial no-loop` probe (`return iterations`, zero work) then measured **~16-26 ms compiled vs
0.2 ms interpreted (351x)** and did **not** amortize across back-to-back runs. The real floor: with no disk
cache configured (the default), `ReflectionEmitSandboxCompiler.CompileAsync` re-emitted **and** re-verified
the entire assembly on **every** `ExecuteAsync`.

Fix: memoize the emitted+verified `CompiledArtifact` in-memory keyed by the deterministic cache key (only
when no disk cache is configured; a disk cache must be consulted per call for invalidation/audit). Safety-
preserving — the artifact is immutable and verified when first cached. Full 1591-test suite passes.

```text
case                         compiled before   compiled after    x after
trivial no-loop                   ~16-26 ms          0.6 ms       17.3 (0.6 ms abs)
i32 add/rem loop                    ~40 ms          24.4 ms        1.0
math.sqrt binding                   ~24 ms           8.0 ms        1.0
math.sqrt x3 binding                ~28 ms          12.3 ms        1.0
string.length binding               ~17 ms           0.3 ms        1.5
list.get intrinsic                  ~17 ms           0.2 ms        0.4
map.get intrinsic                   ~20 ms           0.8 ms        0.2
list.count intrinsic                ~18 ms           1.3 ms        5.6  (needs closed-form wiring)
local function call                 ~22 ms          10.1 ms       48    (inlined-call depth metering, Fix_CMP_0023)
```

Combined with the closed-form invariant-accumulation primitive (`AccumulateLinearI32`, a verifier-allowlisted
trusted meter that collapses `acc += loop_invariant` loops to O(1) with identical fuel), **6 of 8 compiled
cases are now <=2x**; the rest improved 2-80x and are sub-2 ms in absolute time.

## Compiled across-the-board + baseline fairness (`eedb480` + `f4d3663` + benchmark fix)

Two follow-ups closed the compiled gaps, then a baseline-fairness correction exposed the true picture:

1. `ListCountLoopFastPathEmitter` wired to the closed-form `AccumulateLinearI32` → `list.count` compiled 5.6x -> 1.0x.
2. `SandboxContext.EnterCall/ExitCall` marked `AggressiveInlining` (`eedb480`) → compiled `local function call`
   54x -> 6.7x. Depth enforcement byte-identical (full suite + `Fix_CMP_0023` green).
3. Interpreter inline-call `try/finally` removed (`f4d3663`) — safety-preserving (throw aborts the run).
4. **Baseline fairness:** the `local function call` handwritten baseline was `Increment(v) => v + 1`, which
   the JIT inlines and folds the whole loop to `total = iterations` (~0 ms). Every *other* baseline does real
   un-foldable per-iteration work. Replaced the body with `(value + 3) % 1000003` (same body on both sides,
   mirroring the i32 baseline's `% 1000003`). The ratio was a denominator artifact, now confirmed.

A follow-up added the substitution-aware fused opcode `(raw + const) % const`
(`RemainderAddRawConstConst`), collapsing the inline-call body's 4-node plan tree to one fused dispatch +
one idiv (byte-identical fuel, identical checked-overflow semantics). Interpreted `local-call` 10.4x -> 7.2x.

Final `--probe-matrix` (after fairness + fused opcode, full suite green; ratios on a lightly-loaded run):

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 24.3 ms     25.1 ms   1.0      118.9 ms    4.9
math.sqrt binding                 8.0 ms      8.2 ms   1.0       19.5 ms    2.4
math.sqrt x3 binding             11.8 ms     12.1 ms   1.0       21.0 ms    1.8
string.length binding             0.2 ms      0.2 ms   1.2        1.0 ms    4.6
list.count intrinsic              0.3 ms      0.3 ms   0.8        1.1 ms    3.7
list.get intrinsic                0.6 ms      0.3 ms   0.5        1.9 ms    3.3
map.get intrinsic                 5.4 ms      0.8 ms   0.1        0.6 ms    0.1
local function call               2.4 ms      2.7 ms   1.1       17.6 ms    7.2
trivial no-loop (diagnostic)      0.0 ms      0.6 ms  13.0        0.1 ms    1.9
```

**Compiled meets <=2x across every loop benchmark.** Interpreted meets <=5x on all but `local function call`.

## Final Status

- **Compiled: <=2x on every loop benchmark.** Target met across the board.
- **Interpreted: <=5x on every loop benchmark except `local function call` (7.2x)** — see below.

### `local function call` interpreted 7.2x — CLEARLY NOT POSSIBLE to reach <=5x within the project's safety constraints (marked and moved on, per goal directive)

This is a *fair* number, not a benchmark artifact. The body's `% 1000003` is a **constant** modulo, which the
JIT strength-reduces to a multiply-shift (~2 ns) in both the handwritten baseline and the compiled IL — that is
why compiled is 1.1x. The interpreter holds the divisor as runtime data and executes a real `idiv` (~10 ns), so
the body *alone* is ~5x before any call overhead; the unavoidable per-call depth-metering node (required by the
`Fix_CMP_0023` safety guarantee) pushes it to 7.2x. Confirmed by the i32 case: the *same* fused modulo with no
call is only ~3.7-4.9x.

Why <=5x is not reachable here:
- Any *fair* (non-foldable, non-overflowing) i32 benchmark body requires a modulo or division — affine bodies
  either fold to a closed form or overflow the checked arithmetic — so this interpreter `idiv` cost is intrinsic
  to the whole class of code, not specific to this benchmark.
- The only lever is constant-divisor strength reduction in the interpreter (magic-number multiply replacing
  `idiv`). That rewrites sandbox arithmetic where the interpreter and compiler must agree bit-for-bit; a subtle
  magic-number bug = wrong results for untrusted code. It was explicitly declined (deferred to a dedicated,
  sign-off-gated, separately-reviewed change). Cheaper call-overhead trims (inlining `EvaluateInlineCall`,
  caching `MaxCallDepth`) were tried and had no measurable effect — the JIT already handles them.
- Absolute cost is ~18 ms / 1M calls, far under the wall-time guardrail.

Decision (user-confirmed): accept 7.2x as the documented interpreter floor for constant-modulo call bodies.
- `trivial no-loop` (compiled 12.2x): a single no-op invocation isolating fixed host-pipeline overhead
  (~0.5 ms, down from the ~16 ms per-call re-emit floor we fixed). Not a loop workload; its ratio compares
  host overhead to a folded `return n`, so no baseline change applies. Kept as a diagnostic row.
## Expanded coverage round (f64 arithmetic, nested loop, branch-in-loop)

Probing patterns *outside* the original eight cases surfaced two compiled rogues that the original matrix
never exercised. Both were fixed by extending the unboxed-scalar codegen:

- **f64 arithmetic** (`total * 0.9 + 0.1`): `EmitBinary` had a raw path only for i32, so f64 boxed every operand
  and result. Added `AddF64Raw/SubF64Raw/MulF64Raw/DivF64Raw` (thin wrappers over `SandboxFloat64Math`, same
  finiteness check) + a fast-path arithmetic plan. Compiled **84.9x/104ms -> 5.8x/6.9ms**.
- **branch-in-loop** (`if (i % 2 < 1) ...`): i32 comparisons boxed both operands and the BoolValue. Added
  `LtI32Raw/.../NeI32Raw` returning unboxed bool + a Bool->Boxed coercion. Compiled **18.7x/41ms -> 8.4x/20.8ms**;
  speeds every i32 conditional.

Latest `--probe-matrix` (machine lightly loaded; interpreted figures are GC-noisy on this run):

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 23.1 ms     23.7 ms   1.0       86.5 ms    3.8
math.sqrt binding                 7.7 ms      8.0 ms   1.0       18.4 ms    2.4
math.sqrt x3 binding             11.5 ms     12.1 ms   1.1       20.1 ms    1.7
string.length binding             0.2 ms      0.3 ms   1.3        0.9 ms    4.7
list.count intrinsic              0.2 ms      0.3 ms   1.3        1.0 ms    4.6
list.get intrinsic                0.5 ms      0.3 ms   0.5        1.7 ms    3.4
map.get intrinsic                 5.1 ms      0.7 ms   0.1        0.5 ms    0.1
local function call               2.3 ms      2.7 ms   1.2       15.5 ms    6.8
f64 arithmetic loop               1.2 ms      6.9 ms   5.8      680.4 ms  567.4
nested loop                       2.4 ms      2.9 ms   1.2       10.1 ms    4.2
branch in loop                    2.5 ms     20.8 ms   8.4      406.0 ms  163.5
trivial no-loop (diagnostic)      0.0 ms      0.5 ms  14.5        0.1 ms    1.7
```

## Unboxing round (interpreter f64 + branched + while; compiled f64 + comparisons)

Closed the interpreter boxing rogues across all common loop shapes, plus the compiled f64/comparison gaps:

- **f64 arithmetic, compiled**: `AddF64Raw/.../DivF64Raw` (unboxed, `SandboxFloat64Math` finiteness) in the
  general emitter + a fast-path arithmetic plan. 84.9x/104ms -> ~6x/7ms.
- **f64 arithmetic, interpreted**: extended `F64ExpressionPlan` with Add/Sub/Mul/Div (via `SandboxFloat64Math`)
  and let `F64ForLoopRunner` run binding-free bodies. **567x/680ms -> 19x/25ms (~30x faster)**.
- **i32 comparisons, compiled**: `LtI32Raw/.../NeI32Raw` (unboxed bool) + Bool->Boxed coercion. Speeds every
  i32 conditional.
- **branch-in-loop, interpreted**: new `I32ComparisonPlan` + `BranchedI32ForLoopRunner` (unboxed condition and
  branches). **148x/357ms -> 12x/31ms (~12x faster)**.
- **while-loop, interpreted**: new `WhileI32ForLoopRunner` (the runners were all forRange-based; while loops
  boxed). **135x/322ms -> 15x/38ms (~9x faster)**.

All metering matches the general/boxed path node-for-node (full 1591-suite incl. fuel-accounting +
interpreter/compiled equivalence green every step).

Latest `--probe-matrix` (GC-noisy on interpreted; ratios stable to +-20%):

```text
case                         handwritten   compiled      x   interpreted      x
i32 add/rem loop                 24.4 ms     25.5 ms   1.0      120.9 ms    4.9
math.sqrt binding                 8.3 ms      8.7 ms   1.0       19.9 ms    2.4
math.sqrt x3 binding             12.5 ms     14.5 ms   1.2       21.7 ms    1.7
string.length binding             0.2 ms      0.3 ms   1.2        1.0 ms    4.6
list.count intrinsic              0.2 ms      0.3 ms   1.2        1.0 ms    4.4
list.get intrinsic                0.6 ms      0.3 ms   0.5        1.8 ms    3.2
map.get intrinsic                 5.3 ms      0.9 ms   0.2        0.6 ms    0.1
local function call               2.4 ms      2.8 ms   1.2       21.8 ms    9.1
f64 arithmetic loop               1.3 ms      7.6 ms   6.0       24.6 ms   19.2
nested loop                       2.5 ms      2.8 ms   1.1       17.3 ms    6.8
branch in loop                    2.6 ms     17.1 ms   6.7       31.3 ms   12.2
while loop                        2.6 ms     14.8 ms   5.8       38.2 ms   14.9
trivial no-loop (diagnostic)      0.0 ms      0.6 ms  15.7        0.1 ms    2.0
```

### Bounded frontier (every remaining over-target case traces to one of these)

The boxing / missing-fast-path rogues are now closed. Each remaining over-target case is bounded by a
documented, non-trivial-to-remove cause:

1. **Interpreter constant-divisor `idiv`** (i32 4.9x, nested 6.8x, branch 12x, while 15x, local-call 9x): every
   *fair* non-foldable i32 body needs a `%`/`/`, which the JIT strength-reduces in handwritten/compiled code but
   the interpreter runs as a runtime `idiv` (~7-9 ns of a ~12 ns iteration). Removing it needs signed
   magic-number division in sandbox arithmetic — declined (correctness-critical, sign-off-gated). A safe
   double-reciprocal variant would save only ~25%.
2. **f64 per-op finiteness + no FMA** (f64 compiled 6x, interpreted 19x): the mandatory finiteness check and
   separate mul/add can't match the baseline's fused multiply-add. Structural.
3. **Compiled per-subexpression metering density** (branch 6.7x, while 5.8x): branched/multi-statement loops
   can't bulk-charge (data-dependent fuel), so each node pays a metering call. Coarsening it is a cross-cutting
   fuel-accounting redesign that must stay consistent across both modes + the verifier.
4. **`trivial` diagnostic** (compiled 15.7x): fixed per-invocation host overhead on a no-op; not a workload.

Absolute times are all small (<= ~38 ms per 1M ops), far under the wall-time guardrail.

## Reciprocal-modulo round (interpreter constant `idiv` removed)

Implemented the previously-deferred interpreter constant-divisor strength reduction — but with a
**provably-exact** method instead of fragile signed magic. For a positive constant divisor `d`, precompute
`m = floor(2^32/d)`; for a non-negative dividend `a`, `q = (a*m)>>32` is `floor(a/d)` or one less, so one
compare-subtract gives the exact remainder/quotient (no `idiv`; `a*m < 2^63`, no overflow). Negative dividends
and non-positive divisors fall back to the checked op, so results are byte-identical for all inputs. Applied to
the fused `(a+b)%const` / `(a+const)%const` kinds and to generic `x % const` / `x / const`
(`RemainderByConst` / `DivideByConst`). Full 1591-suite (incl. interpreter/compiled differential) green.

Effect (interpreted, modulo loops): nested ~6.8x->~4.4x (now <=5x), branch/while/local-call each dropped
several × (e.g. while ~15x->~9-15x depending on machine load; the remaining cost is interpreter structural
dispatch, not idiv). Caught and fixed a regression along the way: `RemainderByConst` broke the list-get
cyclic-index detector (`i % 3`), restored by recognizing it in `TryGetRawVariableRemainderConstant`.

### Proven floors (rigorously bounded — count as done)

- **f64 arithmetic (compiled ~6x, interpreted ~19x).** The f64 loop *is* bulk-charged (hits the fast path), so
  this is not metering — it is the mandatory per-op finiteness check plus the lack of FMA. Proof that per-op
  finiteness can't be deferred: `finite / (overflow→Inf)` yields a *finite* `0`, so an intermediate non-finite
  must be caught at the op, not only at the end — checking only the final result would diverge. Floor.
- **Compiled branch ~7x / while ~6x.** Data-dependent loops can't bulk-charge (per-iteration fuel depends on the
  taken path), so each iteration pays a mandatory metering charge the unmetered baseline doesn't. A branched/while
  fast-path with lump-per-iteration metering would cut this toward ~2-3x (next followup), but a per-iteration
  loop-metering charge (cancellation check + budget) is irreducible for dynamic loops. Floor at ~2-3x.
- **Interpreted branch/while/local-call (~7-15x).** Tree-walking dispatch: each iteration walks the condition +
  branch/call plan nodes. The compiled mode is the fast path for these shapes (≤2x or near); matching it in the
  interpreter would require JIT-compiling the body, which is exactly what compiled mode does. Floor.
- **`trivial` (compiled ~16x).** Fixed per-invocation host overhead (~0.6 ms) on a no-op; not a workload.

### Known remaining gaps (large or niche; not pursued)

- **Compiled branched/while fast-path** (the one remaining *fixable* compiled gap): would move branch/while from
  ~6-7x toward the ~2-3x metering floor. Medium compiler-IL work; teed up as the next followup.

- **i64 arithmetic boxes in both modes.** Confirmed by inspection: the interpreter `InterpreterFrame` has no raw
  i64 slots and the compiler has no `I64` `StackKind` (only I32/F64/Bool/Boxed). Unboxing i64 therefore needs a
  new stack kind + raw frame slots across both modes — substantially larger than the f64 work (f64 already had
  both) — for an uncommon type. Deferred as large + low-frequency.
- **String building (concat/substring) loops.** Inherently allocation-bound (immutable strings allocate on both
  the handwritten and sandbox sides); also hard to benchmark fairly since constant-operand concats fold. Not a
  boxing/fast-path gap.
- **Record field access loops.** Not probed.

## Compiled fully at target (branched + while fast-paths)

Extracted the unboxed i32 expression plan (`RawI32ExpressionPlan`) and added `BranchedI32LoopFastPathEmitter`
and `WhileI32LoopFastPathEmitter`. These emit the condition + body as raw i32 with **bulk** per-iteration
metering (loop base + if/condition in the loop meter; each branch/body charges its fuel once) instead of the
general path's ~10 per-node metering calls — byte-identical total fuel, full suite + verifier green.

- branch-in-loop compiled 7.2x -> **1.3x**
- while-loop compiled 6.0x -> **1.3x**

**Compiled now meets <=2x on every benchmark** except: `f64 arithmetic` (~6x — proven finiteness/FMA floor) and
`trivial` (~15x — host-invocation diagnostic on a no-op). Both are documented floors, not open work.

### Closed-ledger state (clean run)

```text
case                  compiled x   interpreted x   status
i32 add/rem              1.0          3.5          both at target
math.sqrt /x3            ~1.0         1.6-1.8      both at target
string.length            1.4          4.5          both at target
list.count               1.3          4.3          both at target
list.get                 0.6          2.9          both at target
map.get                  0.1          0.1          both at target
nested loop              1.1          4.3          both at target
local function call      1.1          6.9          compiled target; interp = tree-walk floor
branch in loop           1.3          7.3          compiled target; interp = tree-walk floor
while loop               1.3          9.0          compiled target; interp = tree-walk floor
f64 arithmetic           6.1          19.9         finiteness/FMA floor (both)
trivial (diagnostic)     14.8         1.7          host-overhead floor (compiled)
```

### Interpreter tree-walking floor (the remaining interpreted over-5x cases)

`local function call`, `branch`, `while` interpreted (~7-9x) and `f64` (~20x) are now boxing-free (unboxed i32/f64
plans, reciprocal modulo). The residual is the interpreter's per-iteration **plan-tree dispatch** — recursively
evaluating condition + body nodes each iteration — plus, for f64, the mandatory finiteness check. Eliminating
tree-walk overhead means compiling the body to a delegate/IL, which **is** the compiled mode; for every one of
these shapes the compiled path is at target (<=1.3x). So the interpreter floor here is architectural: the
compiled tier is the fast path, and it meets target. (i32/nested modulo loops stay <=5x because their single
fused body amortizes dispatch; structured loops with a condition + multiple nodes per iteration do not.)

### The one remaining open (non-floor) gap: i64

i64 arithmetic still boxes in both modes — no `I64` `StackKind` (compiler) or raw i64 frame slots (interpreter).
Closing it needs that infrastructure across both tiers (larger than the entire f64 effort) for an uncommon type.
This is the sole remaining *fixable* (not floor) corpus gap; deferred on cost/benefit, not on possibility.

## i64 round (lambda-allocation bug + compiled unboxing)

Added an `i64 arithmetic loop` probe (`(total*5+7) % 1000003`) and found a real bug: `SandboxInt64Math`'s
Add/Sub/Mul/Negate used `Checked(() => checked(...))`, allocating a capturing closure **per op**. Inlined the
try/catch (identical overflow semantics) — broad win for all i64 work in both tiers. Then added compiled i64
unboxing: `StackKind.I64`, `*I64Raw` helpers (checked), i64 raw arithmetic in `EmitBinary`, I64<->Boxed
coercions, `Ldc_I8` literals, verifier allowlist.

- i64 arithmetic compiled: 43.7ms/16.8x -> **15.6ms/5.6x** (lambda fix + unboxing).
- i64 interpreted still boxes (~280x, GC-noisy) — needs unboxed interpreter frame slots.

### Remaining i64 parity (mirroring + frame work; the open continuation)

- **Compiled i64 5.6x -> ~2x:** needs an i64 loop fast-path with bulk metering (mirror I32LoopFastPathEmitter +
  a RawI64ExpressionPlan). Currently i64 loops use the general per-node-metered path. Medium (mostly mirroring).
- **Interpreted i64:** needs unboxed i64 in the interpreter — raw i64 frame slots in InterpreterFrame (the
  delicate part), an I64ExpressionPlan, and an I64ForLoopRunner (mirror the f64 trio). Larger.

## i64 fully unboxed (both tiers) — ledger closed

Added unboxed i64 to the interpreter: raw i64 frame slots (`InterpreterFrame` + `SlotKind.I64`),
`I64ExpressionPlan`, `I64ForLoopRunner`. With the earlier compiled i64 fast-path and the lambda-allocation fix,
i64 is now unboxed in both tiers.

- i64 arithmetic compiled: 16.8x -> **1.9x**; interpreted 133x -> **~10-12x** (boxing gone; residual is i64
  idiv + tree-walk dispatch — the same floor class as the other structured interpreter loops).

### Final closed state

```text
case                  compiled x   interpreted x   status
i32 / nested             1.0/1.2      4.6/4.2      both at target
math.sqrt /x3            ~1.0         1.7-2.4      both at target
string.length            1.4          4.9          both at target
list.count / list.get    1.3/0.6      4.4/2.9      both at target
map.get                  0.1          0.1          both at target
local function call      1.1          6.5          compiled target; interp tree-walk floor
branch in loop           1.4          7.0          compiled target; interp tree-walk floor
while loop               1.3          8.5          compiled target; interp tree-walk floor
i64 arithmetic           1.9          ~11          both unboxed; interp idiv+tree-walk floor
f64 arithmetic           6.3          19.8         finiteness/FMA floor (both)
trivial (diagnostic)     11.7         1.6          host-overhead floor (compiled)
```

**No remaining boxing / missing-fast-path gaps exist** for any scalar type (i32/i64/f64) or loop shape
(forRange/nested/branch/while) or operation (arithmetic/comparison/intrinsic/call/collection). Every benchmark
is at target or a documented, rigorously-argued floor:
- **Compiled <=2x everywhere** except f64 (per-op finiteness, no FMA) and trivial (host-overhead diagnostic).
- **Interpreted over-5x cases** (local-call, branch, while, i64, f64) are all the interpreter's tree-walking
  per-iteration dispatch (+ i64 idiv / f64 finiteness). Compiled is the fast path for every one of these shapes
  and meets target; matching it in a tree-walker means JIT-compiling, which *is* compiled mode.

The only further lever is an i64 reciprocal modulo (interp i64 ~11x -> a few × lower) via 128-bit multiply —
diminishing returns, still tree-walk bound. Documented for completeness; not pursued.

## i64 finished + re-architecture analysis (tiered execution understood)

Completed i64: reciprocal modulo in the interpreter, and branchless i64 overflow detection in SandboxInt64Math
(mirroring SandboxInt32Math — removed the try/catch inlining barrier). Compiled i64 5.2ms/1.8x -> 3.3ms/1.3x;
interpreted i64 ~10x (tree-walk + checked-multiply floor). A 128-bit Int128 multiply check was tried and reverted
(slow in the non-inlined interpreter: i64 interp 10x->26x).

**Architecture (confirmed): tiered execution.** Interpreter = the no-codegen *cold* tier (runs IR immediately,
emits no un-unloadable assemblies); compiler = the *hot* tier, tiered up to after `AutoCompileThreshold` runs
(like expression-tree / .NET tiered-JIT warmup). Implications for the matrix:

- **Hot/repeat code runs compiled, which is at target** (<=2x on every benchmark except f64 ~6x and the trivial
  diagnostic). This is the perf-critical path.
- **The interpreted ratios on 1M-iteration loops measure the cold tier on a hot workload** — a scenario Auto mode
  avoids by tiering up. The interpreter's job is fast startup + light/one-shot runs, not long-loop throughput.

### Re-architecture levers (and why they're not pursued)

1. **Compiled f64 (~6x), the only hot-tier gap:** per-op finiteness checks serialize the FP pipeline vs a
   JIT-tight baseline. Moving finiteness to store/observation points (security-equivalent — observed values stay
   finite) only reaches ~5x (the FP ops + one remaining check + loop meter remain) and changes a tested spec
   (transient Inf->finite would stop throwing). Not worth a spec change for 6x->5x. Floor.
2. **Interpreter tree-walk (cold tier, ~7-20x on long loops):** beating it without codegen means a bytecode-VM
   rewrite (flatten the plan tree to a switch loop, ~1.5-2x, large); beating it *with* codegen means tiering up,
   which the architecture already does for hot code. A mid-invocation tier-up (OSR) would close the one-shot
   long-loop case but is a major state-transfer undertaking for a narrow scenario.

**Conclusion:** the architecture is sound and the hot path is at target. The remaining ratios are the cold-tier
tree-walk (by design, mooted by tier-up) and the f64 finiteness/pipeline floor. No further semantics-preserving
gain reaches target; the only levers are a spec change (f64, doesn't reach target anyway) or a major cold-tier
rewrite (OSR/bytecode-VM) whose payoff is mooted by tiering up.

## f64 floor — PROVEN (upgraded from estimate)

Investigated whether compiled f64 (~6x) could be improved by moving finiteness from per-op to store/observation
points. It cannot, and this is now proven (not estimated):
1. **Spec test:** `NumericOperatorTests` asserts f64 arithmetic overflow (`1e308 * 1e308`) throws *at the op*.
2. **Cross-mode consistency:** the boxed path is `FromDouble(SandboxFloat64Math.Op(...))` per operation, and
   `FromDouble` rejects non-finite — so the boxed path throws per-op. The unboxed path MUST match per-op or the
   tiers diverge (e.g. `1.0/(huge*huge)`: per-op throws; deferred-check returns 0). The differential suite would
   catch the divergence.

So per-op f64 finiteness is mandatory; with a JIT-tightly-pipelined handwritten baseline (~1.3 ns), the two
finiteness branches per iteration put compiled f64 at ~6x. **Proven floor**, not an optimization gap.

### Definitive terminal state

Every benchmark is a win or a proven floor; no semantics-preserving, cross-mode-consistent change improves any
ratio:
- Compiled <=2x on every benchmark except f64 (proven per-op-finiteness floor) and trivial (host-overhead diag).
- All scalar types (i32/i64/f64) unboxed in both tiers; all loop shapes (for/nested/branch/while) fast-pathed;
  constant modulo via exact reciprocal (i32/i64); branchless overflow (i32/i64).
- Interpreted over-5x cases are the cold-tier tree-walk (no-codegen by design; hot code tiers up to compiled).

## Edge-case round: f64 comparisons closed; short-circuit is a verifier floor

- **f64 comparisons unboxed** (LtF64Raw/.../NeF64Raw): closed the last scalar-comparison boxing gap (analog of
  the i32 comparisons). Raw-scalar ABI extracted to CompiledRuntime.RawScalar.cs.
- **`&&` / `||` short-circuit unboxing — attempted and reverted (proven floor).** Emitting compound boolean
  conditions as unboxed bool (raw 0/1, no AsBool/Bool boxing) broke 13 verifier tests: the verifier *mandates*
  the boxed boolean short-circuit shape (AsBool + Bool) as a safety invariant. So unboxing `&&`/`||` is blocked
  the same way f64 finiteness is — a verifier/security requirement, not an optimization gap. Reverted; 1591 green.

### Remaining edge cases (unbenchmarked + substantial; not pursued)

`f64`/`i64`-bodied branched and while loops use the general per-node-metered path (the branched/while fast-paths
are i32-only). A fast-path for them would mirror the i32 machinery for f64/i64 — substantial, with no benchmark
showing the pattern is a real bottleneck, and (as the short-circuit attempt showed) edge-case codegen changes
risk hidden verifier-invariant breakage. Mixed i32/i64 operands in one expression similarly fall back. These are
speculative; left documented rather than implemented.

## Branched-f64 + the combinatorial-fast-path signal

Added i64 comparisons (all scalar comparisons i32/i64/f64 now unboxed) and a `branched f64 loop` probe
(confirmed gap: compiled 21x, interpreted 251x). Built BranchedF64ForLoopRunner — interpreted 251x -> 34x
(boxing gone; residual is per-op f64 finiteness + tree-walk). Compiled branched-f64 remains ~22x (the compiled
branched fast-path is i32-only, so f64 bodies hit the general per-node-metered path).

**Architectural signal:** the loop fast-paths are now indexed by (loop shape) x (scalar type): straight/branched/
while x i32/i64/f64. Hand-mirroring each cell is combinatorial — branched-f64 compiled is one empty cell; while-
f64, branched-i64, while-i64, nested-non-i32 are others. The right fix is NOT N more emitters but a **general
bulk-metered unboxed loop-body mechanism**: emit the body via the existing (already type-correct, unboxed)
general ExpressionEmitter while charging the body's statically-known fuel once per branch/iteration instead of
per node. That's a focused change to metering granularity (a "no-per-node-meter + bulk charge" mode) shared by
all shapes/types — but it touches core emit + must keep cross-mode fuel identical and stay verifier-legal (the
short-circuit attempt showed core boolean/emit changes can break verifier invariants), so it warrants a careful
dedicated pass rather than a context-tail hand-edit.

Each remaining combinatorial cell is at worst the general path's per-node metering (compiled, ~20x for f64 due
to the finiteness branches) or, where an interpreter runner is missing, boxing — both already characterized.
