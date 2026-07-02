# Follow-up issues

Deferred work that was intentionally left out of the mechanical ShaRPC/Safe-IR -> `DotBoxD.*`
rebrand and the Phase 4 meta-package / acceptance-sample work. Each item is a discrete future task;
none of them block the current green build.

- [ ] **Fluent kernel-authoring API (`DotBoxDKernel.Create<…>`).** Today kernels can be authored as raw
  JSON IR. Design a typed, fluent builder so
  hosts can express a kernel in C# (parameters, locals, loops, bindings) and have it lowered to validated
  IR, instead of hand-writing JSON.

- [ ] **Fluent pushdown API (`client.Pushdown<…>().RunAsync()`).** The pushdown round trip is currently
  modelled as an ordinary service method that runs a kernel server-side. Design a first-class client-side
  fluent surface that submits a kernel + inputs and awaits a single result, hiding the manual
  contract/host plumbing the sample demonstrates.

- [ ] **Extract `DotBoxD.Channels` / `DotBoxD.Channels.Abstractions` from `DotBoxD.Services`.** The
  transport-neutral abstractions (`IRpcChannel`, `ITransport`, `IServerTransport`, single-connection
  transports) currently live inside `DotBoxD.Services`. Split them into a dedicated package so the
  dependency direction becomes `DotBoxD.Services -> DotBoxD.Channels` and `DotBoxD.Transports.* ->
  DotBoxD.Channels`, decoupling transports from the RPC core.

- [ ] **Revisit `DotBoxD.Hosting.Http` naming/placement.** The HTTP hosting project sits under
  `src/Hosting` alongside the sandbox `SandboxHost` stack, which conflates "host a kernel" with "host over
  HTTP". Decide whether it belongs under `Channels`/`Transports`, gets renamed, or is folded elsewhere.

- [ ] **Optional `DotBoxD.Rpc` discovery-alias package.** Many users will search for "RPC" rather than
  "Services". Consider shipping a thin `DotBoxD.Rpc` alias/meta-package that points at `DotBoxD.Services`
  (or `DotBoxD.Services.All`) purely for discoverability.

- [ ] **Public-docs terminology pass.** The code is rebranded, but README/guides/XML-doc prose still mix
  legacy terms (ShaRPC, Safe-IR, plugin) with the new three-pillar vocabulary (Services / Kernels /
  Pushdown). Do a deliberate terminology sweep across public docs.

- [ ] **macOS named-pipe test compatibility (re-add macOS to CI).** The CI build-test matrix is
  ubuntu + windows. macOS is excluded because several transport tests use .NET named pipes, which are
  emulated over Unix domain sockets on macOS with different exception types (e.g.
  `ArgumentOutOfRangeException` vs `ObjectDisposedException` on dispose-during-connect) and timing.
  Make those tests macOS-robust (or gate them by platform), then add `macos-latest` back to the matrix.
  The netstandard2.1 Services/Channels libraries themselves are cross-platform; this is a test-harness gap.

- [ ] **Re-enable lock files + regenerate.** `RestorePackagesWithLockFile` is currently `false` and
  `CentralPackageTransitivePinningEnabled` is off (disabled during the merge + rebrand churn — see
  `Directory.Build.props` / `Directory.Packages.props`). Re-enable both and regenerate the lock files in
  the CI/release phase once project names are final.

- [ ] **De-brand internal analyzer/helper implementation types.** A first de-brand pass stripped the
  redundant `DotBoxD` prefix from over-branded *public* types (exceptions, generated metadata, JSON
  importer/exporter, IPC bridge, plugin analyzer/generator). The following *internal*,
  non-breaking helper types still carry the brand and should be de-branded in a later mechanical pass
  (internal only, so no public-API break and no api-baseline churn):
  `DotBoxDRpcGenerator`, `DotBoxDRpcJsonLowerer`, `DotBoxDRpcTypeMapper`,
  `RpcGeneratedAssemblyCatalog`, `DotBoxDGenerationNames`, and all the analyzer
  `DotBoxD*ExpressionLowerer` / `*ModelFactory` / `*BodyModelFactory` / `*Model` / `*Emitter` /
  `*Promoter` / `*Inliner` / `*Reader` internal helpers. Keep the sanctioned public brand entry
  points untouched (`RpcServiceAttribute`, `RpcMethodAttribute`, `DotBoxDGenerated`,
  `DotBoxDGeneratedExtensions`, `DotBoxDInfo`, `DotBoxDServicesInfo`).
