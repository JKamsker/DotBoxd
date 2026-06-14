# Best-of-performance source heads

This ledger is updated on `integration/best-of-performance` whenever the two
source branches are reviewed for the best-of-performance integration loop.

Use the hashes in **Current reviewed source heads** as the previous cut points
for the next sync. Compare each source branch from the recorded hash, exclusive,
to its current head.

## Current reviewed source heads

- `claude/charming-napier-4d62f4`
  - Reviewed through: `462c109227e19c8e4ec6b463f5abb63723d92805`
  - Previous reviewed head: `79c2494092b6c7d4bee70117e7d85dc729e7e370`
  - Decision: imported into `integration/best-of-performance`
  - Integration tip after import: `395317867463597425b918dddb76105c15cd3a57`
- `perf/improve-performance1`
  - Reviewed through: `e526193bbfa9c7944ebd4302979e953e7c475e1f`
  - Previous reviewed head: `30e2f7263f0bb2328aa984d291ffa2e80875633c`
  - Decision: routed to topic PRs, not directly imported into integration
  - Primary topic branch: `topic/best-of/two-arg-binding-fastpath`

## 2026-06-14 second follow-up sync

Integration base before sync:

- `ce7495543fbc8a21b2ee64bfd2359ce6ead0b986`

Imported from `claude/charming-napier-4d62f4`:

- `96223ab` -> `75469c85277f131a9a78f631723e93bde167de02`: unboxed interpreter branched-f64 loop runner and collapsed loop dispatch
- `462c109` -> `395317867463597425b918dddb76105c15cd3a57`: branched-f64 benchmark result and bulk-metering direction notes

Reviewed from `perf/improve-performance1` and routed to `topic/best-of/two-arg-binding-fastpath`:

- `e526193`: fast-path two-argument compiled binding calls

## 2026-06-14 follow-up sync

Integration base before sync:

- `d4619c95174ef95054f04a62e4a248ade64944b0`

Imported from `claude/charming-napier-4d62f4`:

- `95f6d31` -> `db2ef67e976b37d55a76c5b6e89ba74404bf1b35`: unboxed branched i32 loop runner
- `9122089` -> `aa0ee01a1e075f421e47523829f1ebaa8454f32b`: unboxed while-loop i32 runner
- `6fa3394` -> `a73b98c04b2428367fec574861691c4631c86032`: ledger for unboxing round
- `d08bafc` -> `fd43a768c9ff1caa6cdba3d64956875a949fff9b`: i64/string/record gap notes
- `0e787ea` -> `e0be2d5b9c8de8486002b8960ace802ace1dccd3`: exact reciprocal-multiply remainder for interpreter constant modulo
- `d1e8d70` -> `75226304b1cb37b5cfb591b295d47f015f0bbe3f`: exact reciprocal remainder/divide for generic constant modulo/division
- `f61be14` -> `f6e6b35776f60d589512d75d9e4a27994362fd92`: recognize `RemainderByConst` detector
- `40a0b78` -> `98c5563c7b48dadeb460332e0f81ecf4ef599033`: reciprocal-modulo ledger update
- `266f82c` -> `33fcb58bf56a8e3f02edfe9d4a3204029ef2057e`: compiled branched-loop fast path
- `8e7a48f` -> `e4eed0df2b75ccbd1919b3b3e04926d201347b5d`: compiled while-loop fast path
- `63c5156` -> `709137e56fb5a32fb81bfedf42393bbffdcd73ce`: closed-ledger state
- `ea30b43` -> `2cc0a4a31767567d5613546c2e55a8deceeb9390`: i64 lambda allocation fix and compiled i64 unboxing
- `c087446` -> `8aade16f55d76065377d38af2e070d6530f08a6a`: i64 round ledger update
- `2d016ad` -> `d94d5629dd14f544071f35665d5dcd9f00384802`: compiled i64 loop fast path
- `f420e61` -> `b80ccd44eeb6da6915cdee5f3b4700f67cfe0afd`: unboxed i64 interpreter
- `a1aeb2e` -> `027e9eb065a6f770158ee5fdf27d5fa5a3bd43e7`: closed i64 ledger
- `1ad3119` -> `d2a9bf44d6596161b4a417afbc09ae9cff35b756`: exact reciprocal i64 modulo in the interpreter
- `e39e6b5` -> `41ce548135de6ffdf2752dd346ad75ebc0e5aa06`: branchless i64 overflow detection
- `7f390f0` -> `a8814d0194bfafe3f47f3172e24836867947b590`: i64 finished and tiered-architecture analysis
- `7e939b7` -> `709dd14c3c8047965362d48b49d549fba142a3bb`: f64 floor proof notes
- `157b598` -> `9bf9dab079119ad13c22fb6b18ab6af0b4377a85`: unboxed f64 comparisons and raw-scalar ABI split
- `8042371` -> `f194588332990e20c3648a6eb4057efcbc342cab`: edge-case ledger update for f64 comparisons and short-circuit unboxing
- `79c2494` -> `b2138f71591222ca9b164d322b0cea96708fbb97`: unboxed i64 comparisons

Local integration maintenance:

- `23eafe40fe49c3ba45fce695462755e0c293bbfb`: API baseline refresh for the i64 math implementation source shape.

Reviewed from `perf/improve-performance1` and routed to `topic/best-of/plugin-allocation-trims`:

- `bbcca45`: reuse compiled runtime scalar type singletons
- `1a2c118`: fast-path built-in scalar validation
- `3f7f09a`: fast-path flat scalar value metering
- `09aac7e`: bypass full results for no-audit prepared values
- `1174558`: allocate binding return credits lazily
- `a54752e`: reuse immutable bool sandbox values
- `77b142b`: trim owned snapshot wrapper allocations
- `24991b0`: reuse common i32 sandbox values
- `3fc01d4`: reuse meters for installed no-audit dispatch
- `1ecc99a`: let owned list values expose their own read-only view
- `dc73171`: reuse no-audit sandbox contexts for installed kernels
- `a143ba2`: avoid compiled cache key string allocations
- `5ed3b1b`: cache installed no-audit compiled executables
- `982182b`: reuse no-audit `ShouldHandle` input buffers
- `30e2f72`: reuse no-audit `ShouldHandle` input list wrappers

No new `perf/improve-performance1` PR branch was split out in this round; the new
commits extend the existing plugin allocation/no-audit cache experiment.

## 2026-06-14 sync

Integration base before sync:

- `cfa6566ec207fcc614db9395f7d2d7730a363806`

Imported from `claude/charming-napier-4d62f4`:

- `1c5e79a` -> `74be437e4e1b349f5acc36f5e3c9c1e7b85a426a`: fused interpreter opcode for `(raw + const) % const`
- `75bfdb4` -> `67fe83f4568cd5e8487f132a5ba391fd7b357545`: benchmark ledger update for fused opcode
- `9dc5f14` -> `533b0a2c9bea622555890c562c5eefdd0fafb052`: benchmark ledger feasibility note
- `e36963e` -> `74b842f12ba5212c213c51c8a145211fb70c871d`: f64, nested-loop, and branch-in-loop benchmark probes
- `41d19b2` -> `8109bb1050aa84d7a87ee94095598071cf085191`: general compiled f64 arithmetic unboxing
- `51c3799` -> `061d70930d2adc8c6dd5f7a0da5d28b8e1c0ea3e`: f64 arithmetic loop fast path
- `e69e19f` -> `6aaab73723d739d638d9fdf968c83511db4c5699`: compiled i32 comparison unboxing
- `2d2d789` -> `57149ee84b069bab5199b377afaff4051a796d18`: benchmark ledger update for expanded coverage
- `aea170e` -> `237e2f2377b80ffa805f67f858c3b6bc7fd7812d`: interpreted f64 arithmetic unboxing

Reviewed from `perf/improve-performance1` and routed to topic PRs:

- `0b6d693`: skip audit envelope for no-audit compiled success
- `9f22a12`: use prepared host dispatch for installed kernels
- `e26b1bb`: trim clean plugin message binding work
- `ed1923d`: keep synchronous hook dispatch on the fast path

The `claude/charming-napier-4d62f4` no-finally inline-call experiment remains
outside integration and isolated in `topic/best-of/no-finally-inline-call`.
