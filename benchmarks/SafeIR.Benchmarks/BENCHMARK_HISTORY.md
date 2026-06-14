# SafeIR Benchmark History

This file is the performance ledger for SafeIR interpreter/compiler optimization work.
Each optimization commit should append the benchmark command and the before/after
numbers it used.

All results below are local stopwatch probes on this machine, run in Release mode.
Ratios are relative to handwritten C# measured in the same run. These probes are
intended for regression hunting and directionally comparing implementation steps;
they are not BenchmarkDotNet statistical reports.

## Commands

```powershell
dotnet run -c Release --project benchmarks/SafeIR.Benchmarks -p:UseSharedCompilation=false -- --probe-compiled
dotnet run -c Release --project benchmarks/SafeIR.Benchmarks -p:UseSharedCompilation=false -- --probe-bindings
dotnet run -c Release --project benchmarks/SafeIR.Benchmarks -p:UseSharedCompilation=false -- --probe-matrix
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
| Hoisted `map.get` literal-key lookup | this commit | `--probe-matrix` | `map.get` improved from compiled 155.2 ms / 32.1x and interpreted 149.5 ms / 31.0x to compiled 98.3 ms / 20.3x and interpreted 53.7 ms / 11.1x by resolving the immutable literal-key lookup once and still charging the key literal in the loop. |

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

## Current Gaps

The broad performance target is not met yet. The matrix still exposes several
bad cases, especially map access, local function calls, and tiny operations
where the handwritten timing is sub-millisecond. The current `map.get` adapter
still leaves per-iteration key literal charging in place, so bulk literal
charging or deeper map/key specialization remains necessary.
