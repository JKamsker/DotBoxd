using System.Reflection;
using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// Phase 4 proof for a remote <c>RunLocal</c> chain: only <c>Where</c>/<c>Select</c> lower (to a verified-IR
/// kernel that filters and projects), the <c>RunLocal</c> body stays native client C#, and the projected value
/// is pushed across the IPC boundary per matching event. These tests mechanically enforce the premise that
/// <b>filtering and projection always run server-side and only the projected result crosses the wire</b>
/// (<see cref="ChainAggroEvent"/> is shared with <see cref="HookChainRuntimeTests"/>).
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    // A remote RunLocal chain: Where + Select lower; the native RunLocal terminal records what it receives.
    private const string RemoteRunLocalSource = """
        using System.Collections.Generic;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class RemoteRunLocalUsage
        {
            public static readonly List<string> Received = new();

            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 4)
                    .Select(e => e.MonsterId)
                    .RunLocal((id, ctx) => Received.Add(id));
        }
        """;

    // A whole-event remote RunLocal chain: Where only, no Select; the native terminal receives the EVENT.
    private const string RemoteWholeEventSource = """
        using System.Collections.Generic;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class RemoteWholeEventUsage
        {
            public static readonly List<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent> Received = new();

            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 4)
                    .RunLocal((e, ctx) => Received.Add(e));
        }
        """;

    [Fact]
    public void RunLocal_chain_lowers_to_a_local_terminal_projection_package()
    {
        var package = LowerToPackage(RemoteRunLocalSource);

        var subscription = Assert.Single(package.Manifest.Subscriptions);
        Assert.True(subscription.LocalTerminal);          // marked as a local-terminal (RunLocal) chain
        Assert.Equal("string", subscription.ProjectedType); // projects e.MonsterId -> string
        // A pure projection performs no host send, so it requires no capability.
        Assert.Empty(package.Manifest.RequiredCapabilities);
    }

    [Fact]
    public async Task The_interceptor_registers_the_native_delegate_which_receives_the_decoded_projection()
    {
        var assembly = Compile(RemoteRunLocalSource, enableInterceptors: true);

        // Install callback returns the package id; the RunLocal interceptor must call UseGeneratedLocalChain,
        // which installs the package AND registers the native delegate keyed by that id.
        PluginPackage? installed = null;
        string? subscriptionId = null;
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = new RemoteHookRegistry(
            package =>
            {
                installed = package;
                subscriptionId = package.CallbackSubscriptionId ?? package.Manifest.PluginId;
                return ValueTask.FromResult(subscriptionId);
            },
            localHandlers);

        var usage = assembly.GetType("ChainSample.RemoteRunLocalUsage")!;
        usage.GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [registry]);

        Assert.NotNull(installed);          // package installed (UseGeneratedLocalChain, not a throw)
        Assert.NotNull(subscriptionId);
        Assert.True(Assert.Single(installed!.Manifest.Subscriptions).LocalTerminal);

        // Deliver a server-pushed projected value: it must decode and reach the native RunLocal delegate.
        // (UseGeneratedChain would NOT have registered a handler, so this dispatch would throw instead.)
        await localHandlers.DispatchAsync(
            subscriptionId!,
            EncodeString("monster-7"),
            new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));

        var received = (List<string>)usage
            .GetField("Received", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        Assert.Equal("monster-7", Assert.Single(received));
    }

    [Fact]
    public async Task The_server_filters_and_projects_then_pushes_only_the_projected_value_for_matching_events()
    {
        var package = LowerToPackage(RemoteRunLocalSource);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        // Installing runs the verifier over the value-returning Handle entrypoint — proving it verifies.
        var kernel = await server.InstallAsync(package);

        var pushedSubscriptions = new List<string>();
        var pushedPayloads = new List<byte[]>();
        RemoteLocalPush push = (subscriptionId, payload, _) =>
        {
            pushedSubscriptions.Add(subscriptionId);
            // Snapshot eagerly: the payload aliases a pooled buffer the push path returns once this completes,
            // exactly as a real transport copies the bytes into its frame during the send.
            pushedPayloads.Add(payload.ToArray());
            return ValueTask.CompletedTask;
        };
        server.Hooks.On<ChainAggroEvent>().UseProjecting(kernel, "sub-1", push);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-7", 3));   // 3 <= 4 → matches
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-9", 10));  // 10 > 4 → filtered server-side

        // PREMISE: the filter ran server-side BEFORE any IPC, so exactly one of two events crossed the wire.
        Assert.Equal("sub-1", Assert.Single(pushedSubscriptions));
        Assert.Single(pushedPayloads);
        // PREMISE: a projection terminal performs no host send — the only thing produced is the pushed value.
        Assert.Empty(messages.Messages);

        // PREMISE: only the PROJECTED value (MonsterId) crossed, not the raw event — it decodes on the client
        // back to exactly the projection.
        var clientRegistry = new RemoteLocalHandlerRegistry();
        string? received = null;
        clientRegistry.Register<string>("sub-1", (id, _) =>
        {
            received = id;
            return ValueTask.CompletedTask;
        });
        await clientRegistry.DispatchAsync(
            "sub-1",
            pushedPayloads[0],
            new HookContext(messages, CancellationToken.None));
        Assert.Equal("monster-7", received);
    }

    [Fact]
    public void Whole_event_chain_lowers_to_a_local_terminal_event_projection()
    {
        var package = LowerToPackage(RemoteWholeEventSource);

        var subscription = Assert.Single(package.Manifest.Subscriptions);
        Assert.True(subscription.LocalTerminal);     // a local-terminal (RunLocal) chain
        Assert.Equal("record", subscription.ProjectedType); // no Select => explicit whole-event projection
        Assert.Empty(package.Manifest.RequiredCapabilities);
    }

    [Fact]
    public async Task The_server_filters_then_pushes_the_whole_event_record_for_matching_events()
    {
        var package = LowerToPackage(RemoteWholeEventSource);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package);

        var pushedPayloads = new List<byte[]>();
        RemoteLocalPush push = (_, payload, _) =>
        {
            pushedPayloads.Add(payload.ToArray());
            return ValueTask.CompletedTask;
        };
        server.Hooks.On<ChainAggroEvent>().UseProjecting(kernel, "sub-we", push);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-7", 3));   // 3 <= 4 → matches
        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-9", 10));  // 10 > 4 → filtered server-side

        // PREMISE: the filter ran server-side BEFORE any IPC — exactly one of two events crossed the wire.
        Assert.Single(pushedPayloads);
        // PREMISE: a whole-event push performs no host send.
        Assert.Empty(messages.Messages);

        // PREMISE: the WHOLE event record crossed (all fields), round-tripping to the original on the client —
        // proves the value-writer field order matches the marshaller's record reconstruction order.
        var clientRegistry = new RemoteLocalHandlerRegistry();
        ChainAggroEvent? received = null;
        clientRegistry.Register<ChainAggroEvent>("sub-we", (evt, _) =>
        {
            received = evt;
            return ValueTask.CompletedTask;
        });
        await clientRegistry.DispatchAsync(
            "sub-we",
            pushedPayloads[0],
            new HookContext(messages, CancellationToken.None));
        Assert.Equal(new ChainAggroEvent("monster-7", 3), received);
    }

    [Fact]
    public void RunLocal_chain_still_emits_index_predicate_metadata_from_its_where()
    {
        // The convergence must not drop index metadata for RunLocal chains — the Where lowers identically to a
        // .Run chain, so the indexable comparison is extracted onto the manifest.
        var package = LowerToPackage(RemoteRunLocalSource);
        var subscription = Assert.Single(package.Manifest.Subscriptions);

        var predicate = Assert.Single(subscription.IndexedPredicates);
        Assert.Equal("Distance", predicate.Path);
        Assert.Equal(IndexPredicateOperator.LessThanOrEqual, predicate.Operator);
        Assert.Equal(4, Assert.IsType<int>(predicate.Value));
        Assert.Equal("int", predicate.ValueType);
        Assert.True(subscription.IndexCoversPredicate);  // the Where is exactly this indexable comparison
    }

    [Fact]
    public void Eligible_local_chains_emit_a_generated_reflection_free_decoder()
    {
        // Projection chain (e.MonsterId -> string): the package exposes a strongly-typed reader that reads the
        // scalar straight off the wire value — no SandboxValue, no reflection — and the interceptor passes it as
        // the 3rd UseGeneratedLocalChain argument.
        var projection = GeneratedSource(RemoteRunLocalSource);
        Assert.Contains(
            "public static string ReadProjected(global::DotBoxD.Plugins.KernelRpcValue value)",
            projection,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static string ReadProjectedPayload(global::System.ReadOnlyMemory<byte> payload)",
            projection,
            StringComparison.Ordinal);
        Assert.Contains(".Create(), handler, ", projection, StringComparison.Ordinal);
        Assert.Contains(".ReadProjectedPayload)", projection, StringComparison.Ordinal);

        // Whole-event chain (the event record itself): the generated reader reconstructs the DTO via its real
        // constructor, so even the whole-event decode is reflection-free.
        var wholeEvent = GeneratedSource(RemoteWholeEventSource);
        Assert.Contains(
            "ReadProjected(global::DotBoxD.Plugins.KernelRpcValue value)",
            wholeEvent,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReadProjectedPayload(global::System.ReadOnlyMemory<byte> payload)",
            wholeEvent,
            StringComparison.Ordinal);
        Assert.Contains(".Create(), handler, ", wholeEvent, StringComparison.Ordinal);
    }

    private static byte[] EncodeString(string value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(string));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }

    private static PluginPackage LowerToPackage(string source)
    {
        var assembly = Compile(source, enableInterceptors: true);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        var package = (PluginPackage)packageType
            .GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        return LocalTerminalIdentity.IsLocalTerminal(package.Manifest)
            ? LocalTerminalIdentity.WithCallbackSubscriptionId(package, LocalTerminalIdentity.CreateCallbackSubscriptionId())
            : package;
    }

    private static SandboxPolicy ChainPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
