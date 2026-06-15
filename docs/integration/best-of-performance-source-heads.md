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

## DotBoxD 2026-06-15 port replay

DotBoxD replayed the raw SafeIR range
`7272feb086b75b0f2166a7339ec7fe6ee28a7f6d..ff6c221c5707b2851f9f9428a46e7526c6cd42d4`
onto `main` at `82692a2e85d25efbf257c656f968bb93ae061e0f`. The replay preserves
one target commit per SafeIR source commit, with `Ported-from` trailers on each
target commit.

- `b8302592647f4699aa40db1d50d0f9430ee85370` -> `df8efad125e700a04f984c8b9a97074319116d0f`: perf: make list.add/map.set O(n log n) via incremental charging + structural sharing
- `d024cce94d776e994d02896d0e5c2bb574917044` -> `a078717f0bd3c1bfb3be5cd00a934eaa8fa37a04`: exp(closed-form-accumulation): validated verifier-safe bulk-charge primitive (dormant)
- `11ace39c242a3d96c538b0a7e34b366ce1f9949f` -> `48841f61d4c1ef5df3b5b62e0a26a571694366f6`: perf: cache emitted compiled artifacts in-memory (kills the ~16ms per-execution floor)
- `214fc4ef7b2c53f6684849d2b46fb5888ae0932c` -> `eee7fa4f2c3f9eb3742bcde82099f8183616c2d0`: docs: record the in-memory artifact cache floor fix and corrected diagnosis in the benchmark ledger
- `768a16b514227e09460df4882b92419cfc0b4089` -> `99ef86d941f40a596f5e55ed4560c0f8ad88a843`: perf: closed-form list.count accumulation loops (5.6x -> 1.0x compiled)
- `917bffa42a6566524c2e40bf716733fbbe2c6a1a` -> `b6bae85843915961373aa9d6688b16b5fe9656eb`: perf: inline SandboxContext.EnterCall/ExitCall (compiled local-call 54x -> 6.7x)
- `f0d09500ef29e582a55772be59abb341e10d5179` -> `6938d04a583550d18cd74dcf75f536d7b98f9f8e`: test: fair local-call benchmark baseline (real un-foldable body) + ledger
- `cfa6566ec207fcc614db9395f7d2d7730a363806` -> `40e637fe2df43eaab93cab0e0ac55ad55e185e48`: Use bounded LRU cache for reflection artifacts
- `74be437e4e1b349f5acc36f5e3c9c1e7b85a426a` -> `fbcffc89ef50793bf17c0c9e0f580260ee5e445c`: perf: fused interpreter opcode (raw + const) % const, substitution-aware
- `67fe83f4568cd5e8487f132a5ba391fd7b357545` -> `1baf148a851cb673d6aefd9ba738cfe896279aea`: docs: update ledger with fused-opcode result (interpreted local-call 10.4x -> 7.2x)
- `533b0a2c9bea622555890c562c5eefdd0fafb052` -> `ef97543e576f035562b0c71d7ec606bdf395d197`: docs: mark interpreted local-call <=5x as clearly-not-possible within safety constraints
- `74b842f12ba5212c213c51c8a145211fb70c871d` -> `7a8fb96bb8640a5abc266966765b2ed8c6747681`: test: add f64-arithmetic, nested-loop, and branch-in-loop matrix probes
- `8109bb1050aa84d7a87ee94095598071cf085191` -> `eb16c130551c5d18b1e7d65928cb1925a9aa4428`: perf: unboxed f64 arithmetic in the general compiled path (f64 loop 85x -> 10x)
- `061d70930d2adc8c6dd5f7a0da5d28b8e1c0ea3e` -> `86ea2f81f33ba1cff8c0667e40dbdff09f7f0e87`: perf: f64 arithmetic loop fast-path (compiled 10.4x -> 5.7x)
- `6aaab73723d739d638d9fdf968c83511db4c5699` -> `5575c8b301b8903a1f014805bfa1ef9f1065478e`: perf: unboxed i32 comparisons (branch-in-loop compiled 18.7x -> 8.4x)
- `57149ee84b069bab5199b377afaff4051a796d18` -> `8e87c2d3b0115342f107e1b15219425caa86ac3d`: docs: ledger for expanded-coverage round (f64 arithmetic, branch) + remaining gaps
- `237e2f2377b80ffa805f67f858c3b6bc7fd7812d` -> `da45f9592a0dd0d55ca01c586b5a8b0d53b4dc66`: perf: unboxed f64 arithmetic in the interpreter (f64 loop interpreted 567x -> 19.5x)
- `a98959877f841fcf28ebe5da1ec14ff8ecca3cc2` -> `999d9fb7c28f594ac5410e2d2cdc5341e729ebef`: Record best-of-performance source heads
- `5aedd296c45878738ee1fbd38f1eaeec88f589e0` -> `7ad31975690200808034f3ea1e4589691a19dae3`: Fix integration CI baselines and resource test deadlines
- `c479d2c2ea94121dac5e26bc827bf49918f85d7e` -> `ff520a96950c67930e1f0ecaf337e3aaad61d4fa`: Stabilize fake-network tests under CI load
- `d2cde65e0c890d8e2daa85cd7a136a5d0776236c` -> `92b0376c6b2d2cd2116a16f66bca3d1b51388176`: Run CI for integration branches
- `787ee4c55232652f0c4eb1f9ae84175617e5ec53` -> `baa1770daf191ee028a4f0338b710cb60a72f39a`: Update release readiness emitter evidence
- `004396baca41697446c76015c596293d7847fb17` -> `32a4e8773348890b00b47db8022f8da3e18dfbb6`: Serialize safe file publish checks
- `d4619c95174ef95054f04a62e4a248ade64944b0` -> `9a5ab46bbe3fbb9deb2cde475cdad116c2ff8975`: Harden CI-sensitive timeout tests
- `db2ef67e976b37d55a76c5b6e89ba74404bf1b35` -> `f93615ad26ac2592e8d689da8d833730db2fbb9b`: perf: unboxed branched i32 loop runner (branch-in-loop interpreted 148x -> 8.4x)
- `aa0ee01a1e075f421e47523829f1ebaa8454f32b` -> `804e93346387776c10e4464a540ae9990584e2f6`: perf: unboxed while-loop i32 runner (while-loop interpreted 134.8x -> 14.9x)
- `a73b98c04b2428367fec574861691c4631c86032` -> `c66de83e738c1426d0a3cd4b25beb1989c5ab921`: docs: ledger for unboxing round + bounded-frontier analysis
- `fd43a768c9ff1caa6cdba3d64956875a949fff9b` -> `9ebbde4398b1323e80364699978b6d70b076f9bd`: docs: record i64 (large + niche) and string/record gaps in ledger
- `e0be2d5b9c8de8486002b8960ace802ace1dccd3` -> `700bb0d5eb9be076a4ecd6a696497d60c146ddc6`: perf: exact reciprocal-multiply remainder for interpreter constant modulo (no idiv)
- `75226304b1cb37b5cfb591b295d47f015f0bbe3f` -> `034e109683f99f8a138476eb73b5011d9d62fd5a`: perf: exact reciprocal remainder/divide for generic `x % const` and `x / const`
- `f6e6b35776f60d589512d75d9e4a27994362fd92` -> `de98405c358a74b379c6882e7baf7ab50f530b8a`: fix: recognize RemainderByConst in TryGetRawVariableRemainderConstant (restores list.get cyclic-index fast path)
- `98c5563c7b48dadeb460332e0f81ecf4ef599033` -> `d3a1c6f372ee5c5cd4eb59f6904b48c8b73cb8d2`: docs: reciprocal-modulo round + rigorous proven-floor entries in ledger
- `33fcb58bf56a8e3f02edfe9d4a3204029ef2057e` -> `1d611cc777b0d50f979639f2cb38c7eb992ff69e`: perf: compiled branched-loop fast-path (branch-in-loop compiled 7.2x -> 1.3x)
- `e4eed0df2b75ccbd1919b3b3e04926d201347b5d` -> `6e3d270833146e92df7359f5d7c3f5406211bbd3`: perf: compiled while-loop fast-path (while-loop compiled 6.0x -> 1.3x)
- `709137e56fb5a32fb81bfedf42393bbffdcd73ce` -> `63c0f50c1d63572c4f06e723d42b862ada565c89`: docs: closed-ledger state - compiled fully at target; interpreter tree-walk floor; i64 the lone open gap
- `2cc0a4a31767567d5613546c2e55a8deceeb9390` -> `5860fabe0b80057b5ce7c96b6e59d7c3e07ca0b6`: perf: fix i64 arithmetic lambda-allocation + unbox compiled i64 (compiled i64 16.8x -> 5.6x)
- `8aade16f55d76065377d38af2e070d6530f08a6a` -> `5850887db9bd85a864dd362a0645e3c092e33c95`: docs: i64 round (lambda bug + compiled unboxing) + remaining i64 parity scope
- `d94d5629dd14f544071f35665d5dcd9f00384802` -> `67ed709020b7de6f7c143590858b88138a400623`: perf: compiled i64 loop fast-path (i64 arithmetic compiled 5.6x -> 1.9x)
- `b80ccd44eeb6da6915cdee5f3b4700f67cfe0afd` -> `c161f9b5e83bc921509c2aac8e2a2b45f7378afe`: perf: unboxed i64 in the interpreter (i64 arithmetic interpreted 133x -> 10x)
- `027e9eb065a6f770158ee5fdf27d5fa5a3bd43e7` -> `486f85353e28e0f695577a5fdbc7d42bd9999829`: docs: ledger closed - i64 unboxed both tiers; every benchmark at target or proven floor
- `23eafe40fe49c3ba45fce695462755e0c293bbfb` -> `db2c2a788423eb64bcbe6ef727ee677b63553c30`: Update API baseline for i64 math implementation
- `d2a9bf44d6596161b4a417afbc09ae9cff35b756` -> `c74093d1e6c89f60163cd73fb9b26821e7fc5463`: perf: exact reciprocal i64 modulo in the interpreter (RemainderByConst, no idiv)
- `41ce548135de6ffdf2752dd346ad75ebc0e5aa06` -> `47aef39fa4a3a906bb02e1958af35a82c760adae`: perf: branchless i64 overflow detection (compiled i64 1.8x -> 1.3x)
- `a8814d0194bfafe3f47f3172e24836867947b590` -> `0b01a37caf24bd8a89da922a73c5f1a09b9a20d7`: docs: i64 finished + tiered-architecture re-architecture analysis (hot path at target; cold-tier levers characterized)
- `709dd14c3c8047965362d48b49d549fba142a3bb` -> `345687040512455b699e2335570da13ecae5d375`: docs: f64 floor proven (spec test + cross-mode consistency mandate per-op finiteness) - terminal state
- `9bf9dab079119ad13c22fb6b18ab6af0b4377a85` -> `9bf7b8df12f109c11a8859762f7463603b83192f`: perf: unboxed f64 comparisons + extract raw-scalar ABI to its own partial
- `f194588332990e20c3648a6eb4057efcbc342cab` -> `b0efd8a3b17c5c3a256b628cfe850160cc487c94`: docs: edge-case round (f64 comparisons closed; short-circuit unboxing is a verifier-mandated floor)
- `b2138f71591222ca9b164d322b0cea96708fbb97` -> `ab2e0f4ed72594fd7c3b20282563cdb7350239aa`: perf: unboxed i64 comparisons (LtI64Raw/.../NeI64Raw)
- `ce7495543fbc8a21b2ee64bfd2359ce6ead0b986` -> `0c98878860e452234ddeeb997407476ed8f5dea2`: Record latest best-of-performance source heads
- `75469c8a75fc1cd9068631b7eedbd6db1c445324` -> `0c51cf09e94e4d3cc4188c90da96eef629e3456a`: perf: unboxed interpreter branched-f64 loop runner + collapse loop dispatch
- `3953178636509ee0e9be71374aa2e48bd449c14d` -> `55e09ef2ccd953e56adacc40525a4604793f5e57`: docs: branched-f64 result + the combinatorial-fast-path signal (general bulk-meter mechanism is the right next step)
- `ff6c221c5707b2851f9f9428a46e7526c6cd42d4` -> `4eb1f8cd99522a40c0545428e35b0aa27ab93fd5`: Record latest source branch sync

Target-only cleanup:

- `7fa91436b15ee3ae1abbac7ff06f5039d2449645`: rewrites the benchmark entry point's lingering SafeIR namespace reference to the DotBoxD namespace.

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
