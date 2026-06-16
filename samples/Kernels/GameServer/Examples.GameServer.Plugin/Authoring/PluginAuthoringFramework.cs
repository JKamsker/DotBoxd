namespace DotBoxD.Kernels.Game.Plugin.Authoring;

using System.Linq.Expressions;
using JetBrains.Annotations;   // [MustUseReturnValue] — makes a dropped builder chain a warning

// ─────────────────────────────────────────────────────────────────────────────────────────────────────────
// FRAMEWORK GLUE — SKETCH ONLY.
//
// These types really live in DotBoxD.Abstractions / DotBoxD.Plugins.Client and are backed by the source
// generator. They are sketched here so this sample reads as a self-contained "target shape" for the
// ergonomics review. They SUPERSEDE today's framework markers (e.g. [Plugin] → [EventKernel]). Do not treat
// this file as the real framework definition; it exists to make the kernels/Program below legible.
// ─────────────────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Marks an <b>event kernel</b> — a sync <c>ShouldHandle</c>/<c>Handle</c> reaction over a domain event
/// service. Renamed from <c>[Plugin]</c> so the kernel roles read as a matched pair with
/// <see cref="ServerExtensionAttribute"/>, and so "Plugin" is reserved for the facade/assembly.
/// <para>The install id <b>defaults to the kebab-cased type name minus the <c>Kernel</c> suffix</b>
/// (<c>GuardianKernel</c> → <c>"guardian"</c>). Pass an explicit id only to pin a protocol id across a
/// class rename — never required.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class EventKernelAttribute(string? id = null) : Attribute
{
    public string? Id { get; } = id;
}

/// <summary>
/// Marks a <b>server-extension kernel</b> that grafts batch/instance methods onto a domain type. The graft
/// target is named once here; the install id derives from the type (same rule as
/// <see cref="EventKernelAttribute"/>). No hand-written service interface and no hand-typed id are needed.
/// <para><b>Control vs instance scope.</b> If <paramref name="grafts"/> is a control
/// (<c>typeof(IMonsterControl)</c>), the method lands on the collection. If it is an instance/handle type
/// (<c>typeof(IMonster)</c>), the kernel is constructed <b>scoped to the addressed instance</b> —
/// <c>Monsters.Get(id)</c> captures the id, the server resolves that instance and injects it, so the grafted
/// method never re-specifies the id.</para>
/// <para><b>Constructor injection.</b> A kernel ctor may take, in any combination: the <b>scoped instance</b>
/// (the graft target, e.g. <c>IMonster</c>), the <b>root world</b> (<c>IGameWorldAccess</c>, for reads beyond
/// the scope), or <b>both</b> — see <c>BlinkKernel</c> (takes both) vs <c>MonsterKillerKernel</c> (control-
/// scoped, takes only the world).</para>
/// <para><b>Capabilities.</b> Awaited world calls inside a kernel compile against the pure
/// <c>IGameWorldAccess</c>, but each requires a server-declared <c>[HostCapability]</c> (it lives on the server
/// impl, not the contract). If a referenced method's capability falls outside the kernel's grantable prefix the
/// generator/analyzer flags it at the call site; otherwise the install fails closed.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ServerExtensionAttribute(Type grafts, string? id = null) : Attribute
{
    public Type Grafts { get; } = grafts;
    public string? Id { get; } = id;
}

/// <summary>
/// Marks a public kernel method as a graft. <paramref name="receiver"/> defaults to the class-level
/// <see cref="ServerExtensionAttribute.Grafts"/> target; <paramref name="name"/> defaults to the method's own
/// name. So the common case is a bare <c>[ServerExtensionMethod]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ServerExtensionMethodAttribute(Type? receiver = null, string? name = null) : Attribute
{
    public Type? Receiver { get; } = receiver;
    public string? Name { get; } = name;
}

/// <summary>
/// Strongly typed live-settings tuner returned by the facade's <c>Get&lt;TKernel&gt;()</c>. Replaces the old
/// <c>SetValuesAsync(Action&lt;TKernel&gt;)</c> draft-mutation lambda: each <see cref="Set"/> takes a
/// member-access expression plus a typed value, so only <c>[LiveSetting]</c> members are settable, "reading"
/// the kernel is unrepresentable, and nothing is silently dropped. The generator constrains the accepted
/// members to the kernel's actual live settings.
/// <para><see cref="ApplyAsync"/> is the terminal call that ships the batch — <c>Set(...)</c> alone buffers
/// nothing. <see cref="Set"/> (and the facade's <c>Get&lt;TKernel&gt;()</c>) are <c>[MustUseReturnValue]</c>,
/// and a small analyzer warns when a chain is not terminated by an awaited <see cref="ApplyAsync"/>, so a
/// forgotten <see cref="ApplyAsync"/> is a build warning, not a silent no-op.</para>
/// </summary>
public interface ILiveSettingsHandle<TKernel>
    where TKernel : class
{
    [MustUseReturnValue]
    ILiveSettingsHandle<TKernel> Set<TValue>(Expression<Func<TKernel, TValue>> member, TValue value);

    ValueTask ApplyAsync(bool atomic = false);
}

/// <summary>
/// Optional one-line host helper for the common single-pipe plugin. Captures the args parsing so the dev's
/// <c>Main</c> does not re-hand-roll it. Devs who need custom host/logging/DI wiring skip it and build the
/// server themselves.
/// </summary>
public static class GamePluginServerHost
{
    /// <summary>
    /// Returns the pipe name from <c>args[0]</c>; <b>throws <see cref="ArgumentException"/></b> on misuse (the
    /// caller writes the message and exits, see <c>Program.Main</c>). Extra trailing flags are ignored.
    /// </summary>
    public static string PipeNameFromArgs(string[] args)
        => args.Length >= 1 ? args[0] : throw new ArgumentException("Usage: <plugin> <named-pipe-name>");
}

// NOTE — the install/lifecycle verbs are emitted ONLY on the generated `GamePluginServer` facade (and its
// per-control wrappers), via the framework `IPluginServer<TWorld>`. They are deliberately NOT on
// `IGameWorldAccess`, so a kernel that gets the world injected sees only domain reads — never an install verb
// that would throw. Sketched (generated) signatures, with the type-safety the review's 2.3 asks for:
//
//   ValueTask<string> Replace<TService, TKernel>() where TKernel : class, TService;  // kernel must implement the service
//   ValueTask<string> Extend<TKernel>()            where TKernel : class;            // graft target from [ServerExtension]
//   ILiveSettingsHandle<TKernel> Get<TKernel>()    where TKernel : class, new();
//
// The `where TKernel : TService` constraint catches a wrong kernel at compile time; the generator/analyzer
// additionally validates that the kernel manifest actually exports `TService` (a DBXK diagnostic), since
// runtime wiring is by manifest subscription. Constraint + manifest check together close gap 2.3.
