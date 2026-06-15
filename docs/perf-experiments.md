# Performance experiments

Each experiment lives on its own `exp/*` branch. The committed baseline (`master`/work branch) keeps only
changes that preserve every safety guarantee. Experiments explore tradeoffs that relax or extend a guarantee;
read each as **"accept this tradeoff → get this improvement."**

## Context: why the compiled "floor" exists

Compiled loops sit on a ~17–40ms / 1M–10M-iteration floor (e.g. `string.length` 83×, `list.count` 84×,
`list.get` 31×, `local-call` 110× vs handwritten). That floor is **mandated by the sandbox's safety design**,
proven across four walls:

1. The verifier requires a per-iteration `ChargeLoopIteration` on every loop back-edge (static CPU-bound
   proof). `VerifierLoopMeteringTests` guard it.
2. Inlined-call depth metering is required and regression-tested (`Fix_CMP_0023`).
3. I32 arithmetic is checked/throwing (`SandboxInt32Math`), so the overflow point is input-dependent and
   loops can't be naively replaced by closed forms.

The matrix benchmarks also run with `Fuel = long.MaxValue`; under any realistic fuel cap the floor is well
within budget. The one genuinely unbounded case — collection building O(n²) — is fixed on the main branch
(`c4e6091`, ~145–2000× faster, near-linear).

## exp/closed-form-accumulation

**Idea:** a loop `for i in [0,N): acc = acc + INV` where `INV` is loop-invariant collapses to
`acc = acc + INV*N` computed in O(1). No loop back-edge → no per-iteration metering requirement.
`CompiledRuntime.AccumulateLinearI32` reproduces the checked-overflow throw point exactly and charges the
identical bulk loop-iteration fuel.

**Accept:** a small, trusted runtime primitive added to the verifier allowlist + meter classification
(`VerificationPolicy`, `GeneratedMethodShapeSignatures`). **No safety guarantee is weakened** — charged
fuel/iterations and the overflow behaviour are identical; the verifier still requires per-iteration metering
on every *actual* loop.
**Get (potential):** `string.length`, `list.count` (loop-invariant accumulation) → ~2× compiled.

**Status / finding:** the primitive and verifier integration are validated (164 verifier/differential/fuel
tests pass). But it is currently **dormant**: the matrix `string.length`/`list.count` loops do **not** use the
dedicated `*LoopFastPathEmitter`s (their match conditions, e.g. `CanUseDirectStringLength`, don't fit the
benchmark's registered bindings) — they run through the **general** for-range emitter. Demonstrating the win
therefore requires detecting invariant-accumulation in the *general* loop and replicating each invariant's
per-iteration fuel exactly (trivial for intrinsics like `list.count`; involves host-call bulk-charging for
bindings like `string.length`). That is genuine Tier-3 optimizer scope.

**Does not help:** `list.get` (`items[i % 3]` depends on `i` → not invariant) or `local-call` (depth metering
is required by `Fix_CMP_0023`). Those cannot reach ≤2× without relaxing a tested safety guarantee.

## Candidate: exp/strided-metering (not yet built)

**Idea:** count loop iterations in a local and flush to the meter every STRIDE iterations instead of every
iteration. Directly attacks the general-loop floor for *all* loop cases at once.
**Accept:** the verifier proves "metered every STRIDE iterations" instead of "every iteration" → bounded CPU
overrun of up to STRIDE iterations between fuel checks (relaxes the strict per-iteration CPU-bound proof).
**Get (expected):** the ~17ms floor drops toward handwritten across string.length/list.count/list.get/i32.
This is the higher-leverage experiment because it targets the general loop the matrix actually uses.
