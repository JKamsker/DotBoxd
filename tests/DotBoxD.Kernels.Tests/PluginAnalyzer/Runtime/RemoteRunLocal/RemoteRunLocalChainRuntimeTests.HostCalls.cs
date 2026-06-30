using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using HostSupport = DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.PluginAnalyzerHostBindingTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// P1 + P2 coverage: a server-side <c>Select</c> that calls a host binding through <c>ctx.Host&lt;T&gt;()</c>,
/// whose RESULT becomes the projected value pushed to a local <c>RunLocal</c>. Covers a scalar host read (P1) and
/// the marquee shape — a host read returning a <see cref="System.Collections.Generic.IReadOnlyList{T}"/> whose
/// <c>.Count</c> is filtered downstream (P1 ctx-call + P2 non-scalar host return + P3 member-chain), with the
/// count discriminating server-side. Exercises the real binding-dispatch path via the probe host harness.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string HostScalarSource = HostPrelude + """
        public interface IScalarWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);
        }

        public static class HostScalarUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select((e, ctx) => ctx.Host<IScalarWorld>().GetValue(e.Zone))
                    .RunLocal((value, ctx) => { });
        }
        """;

    private const string HostListCountSource = HostPrelude + """
        public interface ITagWorld
        {
            [HostBinding("host.probe.getTags", "probe.read.tags", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            System.Collections.Generic.IReadOnlyList<string> GetTags(string id);
        }

        public static class HostListCountUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select((e, ctx) => ctx.Host<ITagWorld>().GetTags(e.Zone))
                    .Where(tags => tags.Count > 1)
                    .RunLocal((tags, ctx) => { });
        }
        """;

    [Fact]
    public async Task Scalar_host_read_in_a_select_projects_to_run_local()
    {
        // .Select((e, ctx) => ctx.Host<IScalarWorld>().GetValue(e.Zone)): the host binding runs server-side and its
        // int result is the projected value pushed to RunLocal. The leading Where discriminates; the matching event
        // carries the binding's value (42) over the wire.
        var payload = await PushFirstMatchingHosted(HostScalarSource, Matching, Filtered);

        Assert.Equal(42, DecodeReflective<int>(payload));
        Assert.Equal(42, DecodeGenerated<int>(HostScalarSource, payload));
    }

    private const string GuidAutoBindingSource = HostPrelude + """
        [global::DotBoxD.Services.Attributes.DotBoxDService]
        public interface IIdWorld
        {
            [HostCapability("probe.read.id", HostBindingEffect.HostStateRead | HostBindingEffect.Allocates)]
            System.Guid GenerateId(string zone);
        }

        public static class GuidAutoBindingUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select((e, ctx) => ctx.Host<IIdWorld>().GenerateId(e.Zone))
                    .RunLocal((id, ctx) => { });
        }
        """;

    [Fact]
    public async Task Guid_returning_auto_binding_in_a_select_installs_and_round_trips()
    {
        // Regression for the DBXK041 mismatch: an auto-binding ([DotBoxDService], no [HostBinding]) returning a
        // Guid is classified as allocating by the runtime, so the manifest must also carry the Alloc effect or
        // install fails. PushFirstMatching installs the package (asserting no effect-mismatch) and pushes the Guid.
        var payload = await PushFirstMatchingHosted(GuidAutoBindingSource, Matching, Filtered);

        Assert.Equal(SampleId, DecodeReflective<Guid>(payload));
        Assert.Equal(SampleId, DecodeGenerated<Guid>(GuidAutoBindingSource, payload));
    }

    [Fact]
    public async Task Host_list_read_in_a_select_with_downstream_count_filters_server_side()
    {
        // The marquee shape: Select((e, ctx) => ctx.Host<ITagWorld>().GetTags(e.Zone)) returns an
        // IReadOnlyList<string> from a host binding (P2), and .Where(tags => tags.Count > 1) reads its size via
        // list.count (P3) — all server-side.
        // The binding returns 3 tags for "crypt" and 1 for anything else, so the downstream Count discriminates: the
        // "crypt" event matches and pushes its list; an otherwise-identical "void" event is filtered before any push.
        var payload = await PushFirstMatchingHosted(
            HostListCountSource,
            Matching,                          // Zone "crypt" -> 3 tags, Count 3 > 1 -> matches
            Matching with { Zone = "void" });  // Zone "void"  -> 1 tag,  Count 1 > 1 -> filtered downstream

        var expected = new[] { "alpha", "beta", "gamma" };
        Assert.Equal(expected, DecodeReflective<IReadOnlyList<string>>(payload));

        var generated = DecodeGenerated<IReadOnlyList<string>>(HostListCountSource, payload);
        Assert.Equal(expected, generated);
        Assert.False(generated is ICollection<string> { IsReadOnly: false });
    }

    // The chain prelude plus the using directives a host-binding interface needs (HostBinding attribute + the
    // SandboxEffect enum).
    private const string HostPrelude = """
        using System;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using Ev = global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;
        namespace ChainSample;
        """;

    // Server-side filter + host-call projection under a host that registers the probe bindings (the scalar
    // host.probe.getValue from the shared support, plus a list-returning host.probe.getTags below), with the
    // probe.read.* capability granted. Captures the single payload pushed for the matching event.
    private static async Task<byte[]> PushFirstMatchingHosted<TEvent>(string source, TEvent matching, TEvent filtered)
    {
        var package = LowerToPackage(source);
        using var server = PluginServer.Create(
            new InMemoryPluginMessageSink(),
            configureHost: AddChainHostBindings,
            defaultPolicy: HostSupport.ProbeReadPolicy());
        var kernel = await server.InstallAsync(package);

        var pushed = new List<byte[]>();
        RemoteLocalPush push = (_, payload, _) =>
        {
            pushed.Add(payload.ToArray());
            return ValueTask.CompletedTask;
        };
        server.Hooks.On<TEvent>().UseProjecting(kernel, "sub", push);

        await server.Hooks.PublishAsync(matching);
        await server.Hooks.PublishAsync(filtered);

        Assert.Single(pushed);
        return pushed[0];
    }

    private static void AddChainHostBindings(SandboxHostBuilder builder)
    {
        HostSupport.AddProbeBindings(builder);     // host.probe.getValue -> 42 (probe.read.value)
        builder.AddBinding(GetTagsBinding());      // host.probe.getTags -> tag list (probe.read.tags)
        builder.AddBinding(GenerateIdBinding());   // host.ChainSample.IIdWorld.GenerateId -> Guid (probe.read.id)
    }

    // An AUTO-binding ([DotBoxDService], no explicit [HostBinding]) returning a Guid. The id is the analyzer's
    // auto-derived route host.{ns}.{Type}.{Method}; the effects include Alloc because the runtime classifies a Guid
    // return as allocating — the manifest must agree (the fix under test) or install fails DBXK041.
    private static BindingDescriptor GenerateIdBinding()
        => new(
            "host.ChainSample.IIdWorld.GenerateId",
            SemVersion.One,
            [SandboxType.String],
            SandboxType.Guid,
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead,
            "probe.read.id",
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, _, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.ChainSample.IIdWorld.GenerateId",
                    CapabilityId: "probe.read.id",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: "entity:id",
                    Fields: context.BindingAuditFields("probe", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromGuid(SampleId));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    // A host binding returning a List<string> whose length depends on the entity id, so a downstream .Count filter
    // has something to discriminate on: "crypt" yields three tags, anything else a single tag.
    private static BindingDescriptor GetTagsBinding()
        => new(
            "host.probe.getTags",
            SemVersion.One,
            [SandboxType.String],
            SandboxType.List(SandboxType.String),
            SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead,
            "probe.read.tags",
            BindingCostModel.Fixed(3),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var id = ((StringValue)args[0]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.probe.getTags",
                    CapabilityId: "probe.read.tags",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: $"entity:{id}",
                    Fields: context.BindingAuditFields("probe", startedAt)));
                var tags = string.Equals(id, "crypt", StringComparison.Ordinal)
                    ? new[] { "alpha", "beta", "gamma" }
                    : ["solo"];
                var values = new SandboxValue[tags.Length];
                for (var i = 0; i < tags.Length; i++)
                {
                    values[i] = SandboxValue.FromString(tags[i]);
                }

                return ValueTask.FromResult(SandboxValue.FromList(values));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });
}
