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

## Current Gaps

- `list.count` compiled 5.6x: would reach <=2x by wiring the closed-form accumulation into
  `ListCountLoopFastPathEmitter` (as already done for `string.length`). Safety-preserving follow-up.
- `local function call` (compiled ~48x, interpreted ~116x): bounded by the inlined-call depth metering that
  `Fix_CMP_0023` requires in both modes; cannot reach the ratio target without relaxing that tested safety
  guarantee. Absolute time (~10-24 ms / 1M calls) is fine under any realistic fuel policy.
- `trivial no-loop` 17x is a ratio artifact of a ~0 ms JIT-folded baseline; 0.6 ms absolute.
