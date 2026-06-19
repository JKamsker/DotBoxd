using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// P4 coverage: anonymous-object projections, both as INTERMEDIATE server-side stages (lowering to the same
/// <c>record.new</c> as a named DTO, fields read via <c>record.get</c>) and as the TERMINAL pushed value. A terminal
/// anonymous projection is wired by a GENERIC interceptor whose type parameters Roslyn infers at the call site (the
/// source never names the anonymous type) and decoded by a generated anonymous-object literal with the same shape.
/// Shares the <see cref="RemoteRunLocalChainRuntimeTests"/> harness.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string AnonymousIntermediateSource = Prelude + """
        public static class AnonymousIntermediateUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new { Id = e.EncounterId, Dist = e.Distance })
                    .Where(x => x.Dist <= 3)
                    .Select(x => x.Id)
                    .RunLocal((id, ctx) => { });
        }
        """;

    private const string AnonymousMultiFieldSource = Prelude + """
        public static class AnonymousMultiFieldUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new { Zone = e.Zone, Score = e.Score, Boss = e.Boss })
                    .Where(x => x.Score > 1_000_000_000L && x.Boss)
                    .Select(x => x.Zone)
                    .RunLocal((zone, ctx) => { });
        }
        """;

    [Fact]
    public async Task Anonymous_intermediate_projection_filters_then_projects_a_named_terminal()
    {
        // Select(e => new { Id, Dist }) builds an anonymous record server-side; .Where(x => x.Dist <= 3) reads its
        // field via record.get; the terminal Select(x => x.Id) projects a NAMED Guid that is the pushed value.
        var payload = await PushFirstMatching(
            AnonymousIntermediateSource,
            Matching,                          // Dist 3 <= 3 -> matches, terminal Id = SampleId
            Matching with { Distance = 4 });   // leading Where passes (4 <= 4) but Dist 4 <= 3 -> filtered

        Assert.Equal(SampleId, DecodeReflective<Guid>(payload));
        Assert.Equal(SampleId, DecodeGenerated<Guid>(AnonymousIntermediateSource, payload));
    }

    private const string AnonymousTerminalSource = Prelude + """
        public static class AnonymousTerminalUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new { Id = e.EncounterId, Zone = e.Zone })
                    .RunLocal((x, ctx) => { });
        }
        """;

    [Fact]
    public void Anonymous_terminal_projection_emits_a_generic_interceptor_that_compiles()
    {
        // The terminal projection is anonymous. Instead of skipping, the generator emits a GENERIC interceptor
        // whose projection slot is a TProjected type parameter that Roslyn binds to the anonymous type at the call
        // site — so the emitted source never names the anonymous type. Compile asserts the generic interceptor is
        // valid C# that actually binds to the intercepted call (the load-bearing guarantee). The decoder is generic
        // too: it returns TProjected and constructs the same anonymous shape with a source-generated object literal.
        _ = Compile(AnonymousTerminalSource, enableInterceptors: true);
        var generated = GeneratedSource(AnonymousTerminalSource);
        Assert.Contains("Intercept_0<TEvent, TCurrent>", generated);
        Assert.Contains("ReadProjected<TProjected>", generated);
        Assert.Contains("return (TProjected)(object)new {", generated);
        Assert.Contains(".ReadProjected<TCurrent>", generated);
    }

    private const string AnonTerminalRoundTripSource = Prelude + """
        public static class AnonTerminalRoundTripUsage
        {
            public static readonly System.Collections.Generic.List<string> Received = new();

            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new { Id = e.EncounterId, Zone = e.Zone })
                    .RunLocal((x, ctx) => { Received.Add(x.Id.ToString() + "|" + x.Zone); });
        }
        """;

    [Fact]
    public async Task Anonymous_terminal_projection_round_trips_to_the_native_run_local_delegate()
    {
        // Full client+server round-trip. The server projects the anonymous { Id, Zone } and pushes it; the generic
        // interceptor wires the native RunLocal delegate, which receives the anonymous instance reconstructed by the
        // generated anonymous-object decoder.
        var payload = await PushFirstMatching(AnonTerminalRoundTripSource, Matching, Filtered);

        var assembly = Compile(AnonTerminalRoundTripSource, enableInterceptors: true);
        PluginPackage? installed = null;
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = new RemoteHookRegistry(
            package => { installed = package; return ValueTask.FromResult(package.Manifest.PluginId); },
            localHandlers);

        var usage = assembly.GetType("ChainSample.AnonTerminalRoundTripUsage")!;
        usage.GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [registry]);
        Assert.NotNull(installed);   // the generic interceptor ran UseGeneratedLocalChain (did not throw)

        await localHandlers.DispatchAsync(
            installed!.Manifest.PluginId,
            payload,
            new HookContext(new InMemoryPluginMessageSink(), System.Threading.CancellationToken.None));

        var received = (System.Collections.Generic.List<string>)usage
            .GetField("Received", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        Assert.Equal($"{SampleId}|crypt", Assert.Single(received));
    }

    [Fact]
    public async Task Anonymous_terminal_projection_runs_end_to_end_through_a_real_server()
    {
        // The fullest behavioral test, exercising the generated source: the generated interceptor installs the
        // generated package into a REAL PluginServer and wires the server's projection push to the local handler
        // registry, so ONE PublishAsync drives the whole pipeline — server-side filter -> anonymous projection ->
        // wire -> generic interceptor -> generated anonymous decoder -> native RunLocal lambda.
        var assembly = Compile(AnonTerminalRoundTripSource, enableInterceptors: true);
        var usage = assembly.GetType("ChainSample.AnonTerminalRoundTripUsage")!;
        var received = (System.Collections.Generic.List<string>)usage
            .GetField("Received", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        received.Clear();

        var sink = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(sink, defaultPolicy: ChainPolicy());
        var localHandlers = new RemoteLocalHandlerRegistry();

        // The install callback installs the generated package into the live server and forwards every server push
        // into the local handler registry that the generated interceptor registers the native delegate against.
        var registry = new RemoteHookRegistry(
            package =>
            {
                var kernel = server.InstallAsync(package).AsTask().GetAwaiter().GetResult();
                var subscriptionId = package.Manifest.PluginId;
                server.Hooks.On<EncounterEvent>().UseProjecting(
                    kernel,
                    subscriptionId,
                    (id, payload, token) => localHandlers.DispatchAsync(id, payload.ToArray(), new HookContext(sink, token)));
                return ValueTask.FromResult(subscriptionId);
            },
            localHandlers);

        // Running the GENERATED interceptor: it redirects .RunLocal(...) to UseGeneratedLocalChain, installing the
        // package and registering the native delegate.
        usage.GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [registry]);

        await server.Hooks.PublishAsync(Matching);   // Distance 3 <= 4 -> projected + pushed -> lambda fires
        await server.Hooks.PublishAsync(Filtered);   // Distance 99 -> filtered server-side -> no push

        Assert.Equal($"{SampleId}|crypt", Assert.Single(received));
    }

    [Fact]
    public async Task Anonymous_intermediate_projection_with_multiple_fields_filters_server_side()
    {
        // A wider anonymous tuple (string/long/bool) filtered on two of its fields, then a named terminal projection.
        var payload = await PushFirstMatching(
            AnonymousMultiFieldSource,
            Matching,                          // Score 9e9 > 1e9 && Boss -> matches, terminal Zone = "crypt"
            Matching with { Boss = false });   // Boss false -> filtered downstream

        Assert.Equal("crypt", DecodeReflective<string>(payload));
        Assert.Equal("crypt", DecodeGenerated<string>(AnonymousMultiFieldSource, payload));
    }

    // P5 fail-safe: a projected DTO whose field is derived in the constructor body (not a constructor parameter)
    // cannot be expressed as record.new — every persisted field must be a passed argument. Rather than silently
    // drop the derived field, the chain fails safe: it is skipped and no projection IR is emitted.
    private const string DerivedFieldSource = Prelude + """
        public sealed class DerivedInfo
        {
            public string Zone { get; }
            public int ZoneLength { get; }     // derived in the constructor, NOT a constructor parameter
            public DerivedInfo(string zone)
            {
                Zone = zone;
                ZoneLength = zone.Length;
            }
        }

        public static class DerivedFieldUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new DerivedInfo(e.Zone))
                    .RunLocal((info, ctx) => { });
        }
        """;

    [Fact]
    public void Dto_with_a_constructor_derived_field_fails_safe_instead_of_dropping_it()
    {
        // DerivedInfo.ZoneLength is set only in the ctor body, so it is not one of record.new's arguments. The chain
        // is skipped (not lowered) rather than emitting a 1-field record that silently omits ZoneLength — and the
        // generated code stays valid. Compile asserts emit success internally.
        _ = Compile(DerivedFieldSource, enableInterceptors: true);
        Assert.DoesNotContain("record.new", GeneratedSource(DerivedFieldSource));
    }

    private const string NonScalarEqualitySource = Prelude + """
        public static class NonScalarEqualityUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.ScoreEvent>().Where(e => e.Scores == e.Scores)
                    .Select(e => e.Threshold)
                    .RunLocal((threshold, ctx) => { });
        }
        """;

    private const string InheritedDtoSource = Prelude + """
        public record BaseInfo(string Zone);
        public sealed record DerivedShape(string Zone, int Distance) : BaseInfo(Zone);

        public static class InheritedDtoUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new DerivedShape(e.Zone, e.Distance))
                    .RunLocal((shape, ctx) => { });
        }
        """;

    private const string ConvertingCtorSource = Prelude + """
        public sealed class ConvertingInfo
        {
            public int Distance { get; }
            public ConvertingInfo(long distance) => Distance = (int)distance;  // param type (long) != field type (int)
        }

        public static class ConvertingCtorUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new ConvertingInfo(e.Distance))
                    .RunLocal((info, ctx) => { });
        }
        """;

    [Fact]
    public void Equality_on_non_scalar_operands_is_rejected_and_the_chain_is_skipped()
    {
        // e.Scores == e.Scores compares two List<int> values. C# `==` is reference equality there, but the sandbox
        // compares structurally — so the predicate's meaning would change. The chain fails safe (skipped), not
        // lowered to a structural list comparison.
        _ = Compile(NonScalarEqualitySource, enableInterceptors: true);
        Assert.DoesNotContain("ReadProjected", GeneratedSource(NonScalarEqualitySource));
    }

    [Fact]
    public void Projection_of_a_dto_that_inherits_public_properties_fails_safe()
    {
        // DerivedShape inherits Zone from BaseInfo; RecordFields (and the runtime marshaller) see only declared
        // members, so the base property would be silently dropped. The chain is skipped instead.
        _ = Compile(InheritedDtoSource, enableInterceptors: true);
        Assert.DoesNotContain("ReadProjected", GeneratedSource(InheritedDtoSource));
    }

    [Fact]
    public void Projection_with_a_converting_constructor_fails_safe()
    {
        // ConvertingInfo's ctor takes a long but the field is int; record.new declares the field's (int) sandbox
        // type while the value flows from the long parameter. The exact param/field type-match guard rejects it.
        _ = Compile(ConvertingCtorSource, enableInterceptors: true);
        Assert.DoesNotContain("ReadProjected", GeneratedSource(ConvertingCtorSource));
    }
}
