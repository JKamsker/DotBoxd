# Follow-up issues

Deferred work that was intentionally left out of the mechanical ShaRPC/Safe-IR -> `DotBoxd.*`
rebrand and the Phase 4 meta-package / acceptance-sample work. Each item is a discrete future task;
none of them block the current green build.

- [ ] **Fluent kernel-authoring API (`DotBoxdKernel.Create<…>`).** Today kernels are authored as raw
  JSON IR (see `samples/Pushdown/DotBoxd.EndToEnd/CartTotalKernel.cs`). Design a typed, fluent builder so
  hosts can express a kernel in C# (parameters, locals, loops, bindings) and have it lowered to validated
  IR, instead of hand-writing JSON.

- [ ] **Fluent pushdown API (`client.Pushdown<…>().RunAsync()`).** The pushdown round trip is currently
  modelled as an ordinary service method that runs a kernel server-side. Design a first-class client-side
  fluent surface that submits a kernel + inputs and awaits a single result, hiding the manual
  contract/host plumbing the sample demonstrates.

- [ ] **Extract `DotBoxd.Channels` / `DotBoxd.Channels.Abstractions` from `DotBoxd.Services`.** The
  transport-neutral abstractions (`IRpcChannel`, `ITransport`, `IServerTransport`, single-connection
  transports) currently live inside `DotBoxd.Services`. Split them into a dedicated package so the
  dependency direction becomes `DotBoxd.Services -> DotBoxd.Channels` and `DotBoxd.Transports.* ->
  DotBoxd.Channels`, decoupling transports from the RPC core.

- [ ] **Revisit `DotBoxd.Hosting.Http` naming/placement.** The HTTP hosting project sits under
  `src/Hosting` alongside the sandbox `SandboxHost` stack, which conflates "host a kernel" with "host over
  HTTP". Decide whether it belongs under `Channels`/`Transports`, gets renamed, or is folded elsewhere.

- [ ] **Optional `DotBoxd.Rpc` discovery-alias package.** Many users will search for "RPC" rather than
  "Services". Consider shipping a thin `DotBoxd.Rpc` alias/meta-package that points at `DotBoxd.Services`
  (or `DotBoxd.Services.All`) purely for discoverability.

- [ ] **Public-docs terminology pass.** The code is rebranded, but README/guides/XML-doc prose still mix
  legacy terms (ShaRPC, Safe-IR, plugin) with the new three-pillar vocabulary (Services / Kernels /
  Pushdown). Do a deliberate terminology sweep across public docs.

- [ ] **Re-enable lock files + regenerate.** `RestorePackagesWithLockFile` is currently `false` and
  `CentralPackageTransitivePinningEnabled` is off (disabled during the merge + rebrand churn — see
  `Directory.Build.props` / `Directory.Packages.props`). Re-enable both and regenerate the lock files in
  the CI/release phase once project names are final.
