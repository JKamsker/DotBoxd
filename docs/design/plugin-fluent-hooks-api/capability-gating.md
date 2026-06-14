# Capability-gated ctx & event properties (design round 4)

Companion to [plan.md](plan.md), [ownership-auth-and-policy.md](ownership-auth-and-policy.md),
[kernel-binding-model.md](kernel-binding-model.md).

**Request.** Gate `ctx` members and event properties with capabilities — optional, default-allow, but
the example should use it. Requested capabilities live in the plugin manifest. Support hierarchical
ids (`game.world.monsters.list`, `game.world.monster.health.get`, `game.world.monster.health.update`,
`game.world.monster.*`). Unauthorized access to a gated member is **denied**, or the plugin is
**disconnected and its kernels unloaded**.

> **Grounding: most of this already exists.** Every host call a kernel makes is a `BindingDescriptor`
> with a `RequiredCapability` ([BindingContracts.cs:58-70](../../../src/DotBoxd.Kernels/Bindings/BindingContracts.cs)).
> `ctx.Messages.Send` lowers to the binding `host.message.send`, which requires capability
> `host.message.write` ([PluginMessageBindings.cs:11-12, 33-34](../../../src/DotBoxd.Plugins/Runtime/PluginMessageBindings.cs)).
> The capability is enforced **twice**: statically (a package that calls a binding whose capability the
> policy does not grant **fails preparation closed** — see the `ServerPolicy` note "Without the
> message-write grant, package preparation fails closed") and at runtime in the binding invoker
> ([PluginMessageBindings.cs:48-54](../../../src/DotBoxd.Plugins/Runtime/PluginMessageBindings.cs)).
> So "gate ctx with capabilities" is the **existing mechanism**; this round adds four things to it.

---

## 1. What is new (four deltas on the existing capability system)

| # | Delta | Where |
|---|---|---|
| A | **Hierarchical / wildcard** capability matching (`game.world.monster.*`) | `SandboxPolicy` capability lookup |
| B | **Gated event properties** (reading `e.MonsterHealth` needs a capability), not just host calls | adapter + analyzer + manifest |
| C | **Manifest-declared required capabilities**, *derived by the analyzer* from what the IR actually uses | manifest + analyzer |
| D | **Deny-or-disconnect** enforcement wired to session ownership | install validation + runtime backstop |

Default-allow is automatic: a ctx member or event property with **no** `[Capability]` annotation is
ungated and freely accessible — exactly today's behavior. Gating is opt-in per member.

---

## 2. `[Capability]` — annotate the gated surface

The host annotates the members it wants to gate. One attribute, two targets:

```csharp
// (a) Gated ctx host services — the example's game-world surface (each method is a binding).
public interface IGameWorldAccess
{
    [Capability("game.world.monsters.list")]        IReadOnlyList<MonsterId> ListMonsters();
    [Capability("game.world.monsters.get")]         MonsterView Get(MonsterId id);
    [Capability("game.world.monster.health.get")]   int  GetHealth(MonsterId id);
    [Capability("game.world.monster.health.update")] void SetHealth(MonsterId id, int hp);
}

// (b) Gated event properties — reading this property requires the capability.
public sealed record MonsterAggroEvent(
    string MonsterId,
    string PlayerId,
    int Distance,
    int MonsterLevel,
    int PlayerLevel,
    [property: Capability("game.world.monster.health.get")] int MonsterHealth);  // gated
```

`ctx` exposes gated services through a host-provided accessor the example binds (e.g.
`ctx.World.GetHealth(id)`); each annotated method is registered as a `BindingDescriptor` whose
`RequiredCapability` is the attribute's id — i.e. it plugs straight into the existing binding model.

---

## 3. Delta A — hierarchical / wildcard capabilities

Capability ids are dotted paths. A **grant** may be a wildcard; a **requirement** is always concrete.

- Grant `game.world.monster.*` matches required `game.world.monster.health.get`,
  `game.world.monster.health.update`, … (one or more trailing segments).
- Grant `game.world.monster.health.get` matches only that exact id.
- `*` (bare) matches everything; useful for the default-trusted local dev identity.

Today `SandboxPolicy.TryGetActiveGrant` is an **exact** dictionary probe
([Policy.cs:119-137](../../../src/DotBoxd.Kernels/Policy.cs)). Extension: when the exact probe misses, test
the requirement against wildcard grants via a small matcher:

```csharp
internal static class CapabilityPattern
{
    // grant "a.b.*" matches id "a.b.c", "a.b.c.d"; grant "*" matches anything; else exact.
    public static bool Matches(string grantPattern, string requiredId)
    {
        if (grantPattern == "*") return true;
        if (grantPattern == requiredId) return true;
        if (grantPattern.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = grantPattern[..^1];               // "a.b." (keep the trailing dot)
            return requiredId.StartsWith(prefix, StringComparison.Ordinal)
                && requiredId.Length > prefix.Length;       // require ≥1 trailing segment
        }
        return false;
    }
}
```

`GrantsCapability(id)` first tries the existing O(1) exact index, then scans wildcard grants (kept in a
small separate list so the common exact path is unchanged). The grant index
([Policy.cs:139-164](../../../src/DotBoxd.Kernels/Policy.cs)) gains a wildcard bucket built once per policy.

> **Security note (flag for critique).** Wildcards widen authority. A grant must still be issued by the
> per-plugin policy resolver ([ownership-auth-and-policy.md](ownership-auth-and-policy.md) §4) — a
> plugin cannot self-grant `*`. The analyzer-derived *required* set is always concrete (no wildcards),
> so the install check is "every concrete requirement matched by some grant", never wildcard-vs-wildcard.

---

## 4. Delta B + C — gated event properties & manifest-derived required capabilities

The kernel is verified IR, so **what it touches is known statically**. The analyzer already lowers
event-property reads and host calls; it gains a pass that **collects the capability of every gated
thing the IR uses**:

- a host-call binding with a `RequiredCapability` → that capability;
- a read of an event property annotated `[Capability("…")]` → that capability.

It emits the union into the manifest as `RequiredCapabilities` (concrete ids, deduped):

```csharp
public sealed record PluginManifest(
    string PluginId,
    string Contract,
    ExecutionMode Mode,
    IReadOnlyList<string> Effects,
    IReadOnlyList<LiveSettingDefinition> LiveSettings,
    IReadOnlyList<HookSubscriptionManifest> Subscriptions,
    IReadOnlyList<string> RequiredCapabilities /* NEW */);
```

This is the "requested capabilities are in the plugin manifest" ask — but **derived, not self-asserted
for trust**: the plugin can't gain authority by listing a capability; the list only *declares what the
IR needs* so the server can check it against the grant. (A signed manifest from management can
additionally carry an *approved* set — see [ownership-auth-and-policy.md](ownership-auth-and-policy.md)
§4.2 requested-vs-granted.)

For a gated event property specifically: the convention adapter omits a gated property from the
marshalled input unless the kernel was granted its capability; if the IR reads `e_MonsterHealth` but
the grant is missing, validation fails before the kernel ever runs (Delta D).

---

## 5. Delta D — deny at install, disconnect at runtime

Two layers, fail-closed:

**Layer 1 — static deny at install (primary).** When a kernel is installed
([PluginServer.InstallAsync](../../../src/DotBoxd.Plugins/PluginServer.cs) →
`PluginPackageValidator.ValidatePrepared`), validate `manifest.RequiredCapabilities ⊆ grantedPolicy`
using the wildcard matcher (§3). Any unmatched requirement → reject the install with a clear
diagnostic (`E-POLICY-CAPABILITY` / `DBXK06x`). The kernel never prepares; nothing runs. This is the
same fail-closed path that already rejects an ungranted `host.message.write`.

**Layer 2 — runtime backstop → disconnect + unload.** The binding invoker still denies at runtime
([PluginMessageBindings.cs:48-54](../../../src/DotBoxd.Plugins/Runtime/PluginMessageBindings.cs)
pattern → `SandboxErrorCode.PermissionDenied`). If a kernel ever hits a capability denial at runtime
(it shouldn't, given Layer 1, so it signals a tampered/desynced package), the server treats it as a
**trust violation**: revoke the kernel and **dispose the owning session**, which unloads *all* that
plugin's kernels and drops the connection — exactly the round-2 ownership machinery
([ownership-auth-and-policy.md](ownership-auth-and-policy.md) §2). One policy hook decides
deny-only vs. disconnect:

```csharp
public enum CapabilityViolationResponse { DenyCall, DisconnectPlugin }
// server policy: default DenyCall; the example uses DisconnectPlugin to demo "kernels unloaded".
```

So the request's "denied **or** disconnect + unload" is a configurable response to a Layer-2 violation;
Layer 1 makes Layer 2 unreachable for honest plugins.

---

## 6. How the example uses it (examples/GameServer)

- The server defines `IGameWorldAccess` with the gated monster operations (§2) and binds it into the
  sandbox as `game.world.*` bindings; it marks `MonsterAggroEvent.MonsterHealth` `[Capability(...)]`.
- `ServerPolicy.CreateCeiling()` grants the guardian identity `game.world.monster.*` (read) but **not**
  `game.world.monster.health.update` — so a guardian that only calms is fine, but a guardian that tries
  to *write* health is denied at install.
- `GuardianKernel` reads `e.MonsterHealth` and calls `ctx.World.GetHealth(id)`; the analyzer derives
  `RequiredCapabilities = [game.world.monster.health.get]`; install succeeds under the `…monster.*`
  grant.
- A second demo kernel that calls `ctx.World.SetHealth(...)` without the update grant **fails to
  install** (Layer 1) — the example prints the denial, showing capability gating working.

---

## 7. Open questions for the critique panel

1. **Gated property: omit vs. reject** (§4) — when a kernel reads a gated property it wasn't granted,
   is rejecting the install enough, or must the adapter also omit the value (defense in depth)?
2. **Wildcard only on grants** (§3) — confirm requirements are always concrete; should authoring a
   wildcard *requirement* be a hard analyzer error?
3. **`DisconnectPlugin` blast radius** (§5) — disconnecting drops *all* the plugin's kernels for one
   violation. Right for the demo; too aggressive for multi-kernel production plugins? Per-kernel revoke
   vs. per-session disconnect as a policy.
4. **Capability id validation** — enforce a charset/segment grammar for ids (lowercase dotted)
   so `game.world.monster.*` and typos are caught at author time?
