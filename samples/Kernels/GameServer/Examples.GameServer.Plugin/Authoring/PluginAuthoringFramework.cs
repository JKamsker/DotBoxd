namespace DotBoxD.Kernels.Game.Plugin.Authoring;

using System.Linq.Expressions;

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
/// Marks a <b>server-extension kernel</b> that grafts batch methods onto a domain control. The graft target
/// is named once here; the install id derives from the type (same rule as <see cref="EventKernelAttribute"/>).
/// No hand-written service interface and no hand-typed id are needed anymore.
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
/// </summary>
public interface ILiveSettingsHandle<TKernel>
    where TKernel : class
{
    ILiveSettingsHandle<TKernel> Set<TValue>(Expression<Func<TKernel, TValue>> member, TValue value);

    ValueTask ApplyAsync(bool atomic = false);
}

/// <summary>
/// Optional one-line host helper for the common single-pipe plugin. Captures the args parsing + usage message
/// so the dev's <c>Main</c> does not re-hand-roll it. Devs who need custom host/logging/DI wiring just skip it
/// and build the server themselves.
/// </summary>
public static class GamePluginServerHost
{
    /// <summary>Returns the pipe name from <c>args[0]</c>, writing a usage message and exiting on misuse.
    /// Extra trailing flags (e.g. demo switches) are ignored.</summary>
    public static string PipeNameFromArgs(string[] args)
        => args.Length >= 1 ? args[0] : throw Usage();

    private static Exception Usage() => new ArgumentException("Usage: <plugin> <named-pipe-name>");
}

// NOTE — the install/lifecycle verbs (Replace / Extend / Get / InvokeAsync / StartAsync /
// HoldUntilShutdownAsync) are emitted ONLY on the generated `GamePluginServer` facade (and its per-control
// wrappers), via the framework `IPluginServer<TWorld>`. They are deliberately NOT on `IGameWorldAccess`, so a
// kernel that gets the world injected sees only domain reads — never an install verb that would throw.
