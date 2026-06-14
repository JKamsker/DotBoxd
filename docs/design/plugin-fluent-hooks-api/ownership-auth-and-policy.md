# Ownership, authentication, signing & per-plugin policy (design round 2)

Companion to [plan.md](plan.md), [plugin-walkthrough.md](plugin-walkthrough.md),
[server-walkthrough.md](server-walkthrough.md).

This doc captures the second review round: five changes requested on top of the fluent-hooks design.
Two are small/cosmetic (§1, §5); three are architecture (§2 ownership, §3 ergonomics, §4 auth+policy).
Everything here is **design**, grounded in the current code — file/line anchors are given so the
proposal can be checked against reality.

> ⚠️ **Superseded in part by [implementation-plan.md](implementation-plan.md) (authoritative).** A
> multi-agent self-review demoted **§4 (auth/signing/per-plugin policy) to a deferred appendix** (no
> consumer in the example; the server owns policy outright), found `peer.OnDisconnected` (§2.4)
> **fictional**, and flagged four §2 concurrency defects. Treat §4 as future design; for what is
> actually being built, follow the implementation plan.

> **Grounding note.** Several requested capabilities already exist in the codebase and only need to be
> *surfaced*, not built. Those are called out as **[already exists]** so we don't re-invent them.

---

## 1. Fluent `Where` / `Select` instead of a `filter:` parameter

**Request.** Prefer `On<>()…Where/Select…UseKernel<T>()` over
`UseKernel<RetaliationKernel>(filter: (e, ctx) => …)`.

**Decision.** Drop the `filter:` overload from `UseKernel`. Pre-kernel gating is expressed in the
chain:

```csharp
// Before (rejected)
await server.Hooks.On<AttackEvent>()
    .UseKernel<RetaliationKernel>(filter: (e, ctx) => e.Damage >= 10);

// After (adopted)
await server.Hooks.On<AttackEvent>()
    .Where((e, ctx) => e.Damage >= 10)
    .UseKernel<RetaliationKernel>();
```

**Semantics (the important part).** A `Where`/`Select` before `UseKernel<T>()` is *plugin-authored
code*, so it is **lowered to verified IR and runs sandboxed** — never native. The analyzer
AND-composes the pre-kernel `Where`(s) into the kernel's gate at **compile time**, emitting one
verified module owned by the same plugin:

```
On<AttackEvent>().Where(p).UseKernel<RetaliationKernel>()
   ──lowered──▶  ShouldHandle = p(e) && RetaliationKernel.ShouldHandle(e)
                 Handle       = RetaliationKernel.Handle(e)
```

This reuses the Phase-C "`Select` = compile-time substitution" philosophy: no new runtime
value-passing protocol, one module contract, the gate runs before the kernel body.

**Type constraint (unchanged from plan B3).** `UseKernel<T>()` is offered only while the flowing
element is still `TEvent` (a kernel consumes the original event via its adapter). `Select` to a
different type *then* `UseKernel<T>()` is a diagnostic (`DBXK1xx`); after a `Select` the terminal must
be `InvokeKernel`/`InvokeLocal`.

**Why this is strictly better than `filter:`** — it unifies the surface (one way to gate), it makes
the gate visibly sandboxed (it sits in the lowered chain), and it composes with `Select`.

---

## 2. Kernel ownership & lifecycle

**Request.** Kernels are owned by a plugin and cannot be hijacked by another plugin. When the plugin
goes away, its kernel goes away too (`IDisposable`, server-side IPC watching).

### 2.1 The vulnerability today

`KernelRegistry` is keyed by `pluginId` **string only**, with no owner identity
([PluginServer.cs:115-209](../../../src/DotBoxd.Plugins/PluginServer.cs)). Worse, `Add` *silently
revokes and replaces* any incumbent with the same id:

```csharp
// PluginServer.cs — KernelRegistry.Add (today)
if (_kernels.TryGetValue(kernel.Manifest.PluginId, out var existing) && !ReferenceEquals(existing, kernel))
    revoke = existing;          // ← any caller reusing "guardian" silently kills the incumbent
_kernels[kernel.Manifest.PluginId] = kernel;
...
revoke?.Revoke();
```

Two concrete problems:
- **Hijack.** Connection B installs a package whose manifest says `pluginId = "guardian"` and
  instantly revokes + replaces connection A's guardian kernel. Ids are self-asserted in the manifest.
- **Leak.** A kernel installed by a connection survives that connection disconnecting — it keeps
  running against the simulation with nobody owning it.

### 2.2 Design: sessions own kernels

Introduce a server-side **`PluginSession : IDisposable`**, one per *authenticated* IPC connection
(identity from §4). A session owns the kernels it installs; disposing it revokes them.

```csharp
namespace DotBoxd.Plugins;

/// <summary>
/// Server-side owner of every kernel installed over one authenticated connection. Disposing the
/// session (on disconnect or host shutdown) revokes and unregisters all kernels it owns.
/// </summary>
public sealed class PluginSession : IDisposable
{
    private readonly PluginServer _server;
    private readonly List<string> _ownedPluginIds = [];
    private int _disposed;

    internal PluginSession(PluginServer server, PluginIdentity identity)
    {
        _server = server;
        Identity = identity;
    }

    public PluginIdentity Identity { get; }

    public async ValueTask<InstalledKernel> InstallAsync(
        PluginPackage package, SandboxPolicy policy, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        // Ownership is enforced here, before anything is installed (see 2.3).
        var kernel = await _server.InstallOwnedAsync(this, package, policy, ct).ConfigureAwait(false);
        lock (_ownedPluginIds) _ownedPluginIds.Add(package.Manifest.PluginId);
        return kernel;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        string[] owned;
        lock (_ownedPluginIds) owned = [.. _ownedPluginIds];
        foreach (var id in owned)
            _server.UninstallOwned(this, id);   // revoke + remove, owner-checked
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
```

### 2.3 Ownership enforcement in the registry

`InstalledKernel` gains an `OwnerId` (stable per session). The registry rejects cross-owner
collisions instead of silently revoking:

```csharp
// KernelRegistry.Add (revised) — fail closed on cross-owner id reuse.
internal void Add(InstalledKernel kernel)
{
    lock (_gate)
    {
        if (_kernels.TryGetValue(kernel.Manifest.PluginId, out var existing)
            && existing.OwnerId != kernel.OwnerId)
        {
            throw new SandboxValidationException([ new SandboxDiagnostic(
                "DBXK060",
                $"plugin id '{kernel.Manifest.PluginId}' is owned by another session and cannot be replaced.") ]);
        }
        _kernels[kernel.Manifest.PluginId] = kernel;   // same-owner reinstall (hot reload) still allowed
    }
    // same-owner replacement still revokes the prior instance (outside the lock)
}
```

> **Design choice — global vs per-owner id namespace.** Keying the dictionary by the bare `pluginId`
> with an owner check (above) keeps ids globally unique *and* prevents collision. The alternative —
> key by `(OwnerId, pluginId)` so two tenants may each have a "guardian" — is a product decision; it
> is called out for the critique panel (§6). The shipped example only needs collision *prevention*,
> so the simpler owner-checked single namespace is the default proposal.

### 2.4 Disconnect = revoke (the "IPC watching" part)

The control service is created **per peer** and bound to a session; when the DotBoxd peer disconnects,
the session is disposed. DotBoxd's `ForEachPeer` gives the per-connection hook
([DotBoxdDotBoxdRpcMessagePackIpc.cs:41-49](../../../src/DotBoxd.Pushdown.Services/DotBoxdDotBoxdRpcMessagePackIpc.cs)):

```csharp
await using var host = DotBoxdDotBoxdRpcMessagePackIpc.ListenNamedPipe(pipeName, peer =>
{
    var identity = authenticator.Authenticate(peer);          // §4
    var session  = pluginServer.CreateSession(identity);
    peer.ProvideGamePluginControlService(new GamePluginControlService(session, sink, world));
    peer.OnDisconnected(() => session.Dispose());             // ← revoke owned kernels on drop
});
```

> **Integration point to verify (flagged for critique).** The exact DotBoxd disconnect hook
> (`peer.OnDisconnected`/session-completion task/`IDisposable` on the peer) must be confirmed against
> the DotBoxd package — it is referenced as a NuGet dependency, not vendored, so the precise name is
> unverified here. If no event exists, fall back to a heartbeat/keepalive timeout owned by the
> control service. The *design* (session disposal revokes owned kernels) is transport-agnostic.

**Revocation already exists.** `InstalledKernel.Revoke()` cancels a per-kernel `CancellationTokenSource`
and every entrypoint checks `IsRevoked` under the execution gate
([InstalledKernel.cs:46-54, 107-178](../../../src/DotBoxd.Plugins/InstalledKernel.cs)). **[already
exists]** — sessions just need to call it on dispose. No new revocation machinery.

---

## 3. Adapter ergonomics — make it C#-native / inferred

**Request.** Is `MonsterAggroEventAdapter` required? Can it be inferred from the type / attributes?

**Answer: it is already optional. [already exists]** `PluginEventAdapterRegistry.Resolve<T>()` falls
back to a `ConventionEventAdapter<T>` when no adapter is registered
([PluginEventAdapterRegistry.cs:15-24, 73-271](../../../src/DotBoxd.Plugins/Runtime/PluginEventAdapterRegistry.cs)).
The convention adapter:
- reads public readable properties in **constructor/metadata order** (so positional records work),
- infers each sandbox parameter name as `e_<PropertyName>`,
- maps CLR types → sandbox types via `LiveSettingTypeConverter`.

So the hand-written adapter in the walkthroughs is pure boilerplate and should be **deleted from the
example**. The events collapse to plain records:

```csharp
// DotBoxd.Kernels.Game.Server.Abstractions — this is the whole thing now.
public sealed record MonsterAggroEvent(string MonsterId, string PlayerId, int Distance, int MonsterLevel, int PlayerLevel);
public sealed record AttackEvent(string AttackerId, string TargetId, int Damage, int AttackerLevel);
```

`server.Hooks.On<MonsterAggroEvent>()` resolves a convention adapter lazily — no registration, no
adapter class.

### 3.1 Two gaps worth closing (proposed, not yet built)

1. **AOT / allocation.** `ConventionEventAdapter` reflects at runtime and compiles a getter expression
   per property ([…EventAdapterRegistry.cs:261-267](../../../src/DotBoxd.Plugins/Runtime/PluginEventAdapterRegistry.cs)).
   For NativeAOT and zero-alloc hot paths, the **analyzer should source-generate** a static adapter
   from the event type (it already inspects the assembly for lowering). The reflection adapter stays
   as the zero-config fallback. Generated adapter is selected automatically via the existing
   `Instance`-property discovery in `TryDiscoverAdapter`.

2. **Cases convention can't infer** — opaque-id branding and custom names. Add optional property
   attributes as the escape hatch (only used when you need them):

   ```csharp
   public sealed record MonsterAggroEvent(
       [property: OpaqueId("MonsterId")] string MonsterId,   // brand as an opaque id type
       [property: OpaqueId("PlayerId")]  string PlayerId,
       int Distance,
       [property: SandboxParam("monster_lvl")] int MonsterLevel,  // explicit param name
       int PlayerLevel);
   ```

   - `[OpaqueId("Foo")]` → emits the property as opaque-id type `Foo` and auto-declares it in the
     policy (`DeclareOpaqueIdType`, [SandboxPolicyBuilder.cs:102-113](../../../src/DotBoxd.Kernels/Policies/SandboxPolicyBuilder.cs)).
   - `[SandboxParam("name")]` → overrides the inferred `e_<Name>`.
   - `[SandboxIgnore]` → drops a property the kernel never reads.

   Without any attribute, today's convention behavior is unchanged.

**Net:** the common case needs zero adapter code; attributes cover the 10% the convention can't see.

---

## 4. Authentication, signing & per-plugin policy

**Request.** Global limits are good, but we also want **per-plugin** limits, **plugin authentication**,
and a credential/cert story:
- named pipe / stdio: the server controls (spawns) the plugin process;
- TCP: must be able to transfer creds/certs;
- a dev gets a cert from server management for local dev;
- certs can sign plugins, and the plugin uses the cert to auth;
- the manifest carries approved limits, **or** a management platform controls both ends and sets
  per-plugin limits.

This is the largest area. The model separates four concerns that are easy to conflate:

| Concern | Question it answers | Mechanism |
|---|---|---|
| **Authentication** | *Who* is connecting? | transport-specific `IPluginAuthenticator` → `PluginIdentity` |
| **Authorization (ceiling)** | What is this principal *allowed*? | `PluginIdentity.ApprovedGrant` (from cert/management) |
| **Integrity** | Is this package *what management approved*? | signed grant envelope over the package hash |
| **Effective policy** | What limits run *this* kernel? | resolver: ceiling ∩ grant ∩ manifest-request |

### 4.1 Authentication — `IPluginIdentity` per transport

```csharp
public sealed record PluginIdentity(
    string Subject,                       // e.g. cert CN, or "local-process:1234"
    string Issuer,                        // CA / management authority, or "host-spawned"
    IReadOnlySet<string> AllowedPluginIds,// ids this principal may install ("*" for dev)
    PluginGrant? ApprovedGrant);          // policy ceiling management approved (null ⇒ server default)

public interface IPluginAuthenticator
{
    PluginIdentity Authenticate(RpcPeer peer);   // throws/!= success ⇒ connection refused
}
```

**Two shipped authenticators, chosen by transport trust model:**

- **`LocalProcessAuthenticator`** (named pipe / stdio, server-spawned). The server *launched* the
  child, so the child is trusted by construction; identity is taken from launch config. Hardening:
  - the pipe name already requires a 128-bit random component
    ([DotBoxdDotBoxdRpcMessagePackIpc.cs:122-142](../../../src/DotBoxd.Pushdown.Services/DotBoxdDotBoxdRpcMessagePackIpc.cs)) —
    keep that (it stops other local processes guessing the pipe);
  - tighten the **named-pipe ACL to the current user** so a different local user can't connect;
  - pass a **one-time bootstrap token** to the child (env var / first stdin line) that it echoes on
    its first call, binding *this* connection to the intended identity (defends against a local race
    to the pipe between spawn and connect).

- **`CertificateAuthenticator`** (TCP / remote). Mutual TLS; the client presents a cert issued by the
  management CA. Validate chain + revocation → map cert claims to `PluginIdentity` (subject = CN,
  `AllowedPluginIds` and `ApprovedGrant` from cert extensions or a management lookup keyed by the
  cert thumbprint). This is the "dev gets a cert from server management" path.

> **Transport note.** TCP/mTLS is not in `DotBoxd.Pushdown.Services` today (pipes only). The
> authenticator interface is transport-agnostic; a `DotBoxd.Kernels.Transport.Tcp.DotBoxdRpc` with mTLS is the
> implementation vehicle and is **explicitly out of scope for the example** — the design just must not
> preclude it. Flagged for the simplicity critic (§6): do we build the interface now or only when TCP
> lands? Proposal: ship the interface + `LocalProcessAuthenticator` now (it's needed for §2 sessions
> anyway); defer `CertificateAuthenticator` to when a TCP transport exists.

### 4.2 Signing — distinguish *requested* from *granted*

The single most important security rule:

> **A self-asserted manifest can only ever request a SUBSET of what the server/identity grants. It can
> never raise its own ceiling.** The manifest's stated limits become *authoritative* only when carried
> in a **signed grant** issued by management.

So we split two things that look similar:

```csharp
// Self-asserted, lives in the package the plugin built. Can only NARROW.
public sealed record PluginManifest(..., PluginResourceRequest? RequestedLimits /* new, optional */);

// Issued and signed by management. The authoritative ceiling. Verified on install.
public sealed record SignedPluginGrant(
    string PackageHash,                   // binds the grant to one exact package (manifest+module)
    PluginGrant Grant,                    // approved limits + capabilities + allowed events
    DateTimeOffset NotAfter,
    byte[] Signature,                     // over canonical(PackageHash | Grant | NotAfter)
    byte[] SignerCertChain);              // chains to a trusted management CA

public sealed record PluginGrant(
    ResourceLimits ApprovedLimits,
    IReadOnlySet<string> ApprovedCapabilities,
    IReadOnlySet<string> AllowedEvents);
```

On install the server: verifies the signature + chain, recomputes the package hash and checks it
equals `SignedPluginGrant.PackageHash` (so the grant can't be lifted onto a different package), checks
`NotAfter`. `PackageHash` reuses the existing canonical hashing approach
(`PolicyHash`/module hashing — [Policy.cs:75](../../../src/DotBoxd.Kernels/Policy.cs) shows the canonical-hash
pattern already in the codebase). **[partially exists]** — canonical hashing exists; the signing
envelope + verification is new.

**Dev loop:** management issues a dev cert; the dev's build signs local packages with it; in dev the
server trusts the dev CA. Same code path as production, friendlier trust root. No special-case "dev
mode" that weakens verification — just a different trusted CA set.

### 4.3 Per-plugin effective policy — a resolver, not one default

Replace `PluginServer`'s single `_defaultPolicy`
([PluginServer.cs:11, 54-58, 81](../../../src/DotBoxd.Plugins/PluginServer.cs)) with a resolver. The
effective policy for a kernel is the **intersection (clamp to the minimum)** of every ceiling:

```csharp
public interface IPluginPolicyResolver
{
    // effective = GlobalCeiling ∩ Identity.ApprovedGrant ∩ SignedGrant ∩ Manifest.RequestedLimits
    SandboxPolicy Resolve(PluginIdentity identity, PluginPackage package, SignedPluginGrant? grant);
}
```

Resolution rules (all **fail-closed**):
- start from the server **global ceiling** (the old `_defaultPolicy` becomes the *maximum*, not the
  default-as-floor);
- clamp each `ResourceLimits` field to the **min** of ceiling / identity grant / signed grant;
- a capability is allowed only if present in **every** applicable grant;
- the manifest's `RequestedLimits` can only further **narrow** (never widen) — a request above the
  approved ceiling is **rejected**, not clamped silently (so misconfig is loud);
- if there is no signed grant and no identity grant, fall back to the global default *as a ceiling*
  and the manifest request narrows within it (this is the "trusted local dev, no signing yet" path).

`ResourceLimits` is already an immutable record with every knob
([ResourceLimits.cs](../../../src/DotBoxd.Kernels/Model/ResourceLimits.cs)), so the clamp is a pure
field-wise `Math.Min` producing a new record — no mutation. `SandboxPolicy` is similarly immutable
with `with` semantics ([Policy.cs:16-75](../../../src/DotBoxd.Kernels/Policy.cs)). **[building blocks
exist]**.

The example's `ServerPolicy.Create()` becomes the **global ceiling** fed to the resolver; per-plugin
narrowing happens automatically from each package's request.

### 4.4 How the four pieces compose on install

```
peer connects
  └─ IPluginAuthenticator.Authenticate(peer) ───────────────▶ PluginIdentity (+ ApprovedGrant)
        └─ PluginServer.CreateSession(identity) ─────────────▶ PluginSession (owns kernels, §2)
              └─ InstallAsync(package, signedGrant?)
                    ├─ verify identity.AllowedPluginIds ∋ package.PluginId      (authz)
                    ├─ verify SignedPluginGrant (sig, chain, hash, NotAfter)    (integrity)
                    ├─ IPluginPolicyResolver.Resolve(...) → effective policy    (per-plugin limits)
                    ├─ PluginServer.InstallOwnedAsync(session, package, policy) (sandbox prepare+verify)
                    └─ kernel tagged OwnerId = session                          (ownership, §2)
```

Each arrow is fail-closed: any failure refuses the install and the kernel never runs.

---

## 5. Server `Program` becomes a full class

**Request.** Mirror the plugin: make the server `Program` a real class.

```csharp
namespace DotBoxd.Kernels.Game.Server;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // … build world + server, baseline phase, listen, launch plugin, with-plugin phase …
        return 0;
    }
}
```

Top-level statements move into `Main`; the exit-code contract (0 / 1) is preserved. The local
`static` helpers (`PlayerHpById`, `TotalDamageTaken`, `PrintWorld`, …) become `private static` methods
on `Program`.

---

## 6. Open questions surfaced for the critique panel

1. **Id namespace** (§2.3): single global id namespace with owner-check, or per-owner `(owner, id)`
   namespace allowing two tenants to each own a "guardian"?
2. **Build the auth interface now or later** (§4.1): is shipping `IPluginAuthenticator` +
   `LocalProcessAuthenticator` before any TCP transport exists justified, or YAGNI until mTLS lands?
   (It is needed for §2 sessions regardless — but how thin should it be?)
3. **Signed grant vs signed package** (§4.2): sign a *grant envelope* that references the package
   hash (proposed), or sign the package itself and put approved limits inside the signed manifest?
   Trade-off: envelope lets management re-issue limits without a rebuild; signed-manifest is simpler
   but couples limits to the build.
4. **Disconnect hook** (§2.4): confirm the real DotBoxd peer-lifecycle API; design must not depend on
   a hook that doesn't exist.
5. **Clamp vs reject** (§4.3): silently clamp an over-broad manifest request, or reject loudly?
   (Proposal: reject, so misconfiguration is visible — but that can break a plugin when management
   *lowers* a ceiling. Maybe: reject at author time via analyzer, clamp at runtime with a warning.)
