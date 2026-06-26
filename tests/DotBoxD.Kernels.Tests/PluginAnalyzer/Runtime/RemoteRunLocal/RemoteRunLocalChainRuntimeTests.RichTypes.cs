using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>An enum event field, exercised so its value (not just 0) round-trips through the I32 wire kind.</summary>
public enum GamePhase
{
    Intro = 0,
    Battle = 2,
    Victory = 7
}

/// <summary>A nested DTO field, exercised for record-in-record fidelity.</summary>
public sealed record PlayerInfo(string Name, int Level);

/// <summary>A DTO projected by a <c>new Dto(...)</c> Select, carrying a Guid alongside a scalar.</summary>
public sealed record EncounterTicket(Guid EncounterId, string Zone);

/// <summary>A DTO whose first field is itself a DTO, exercising a constructed <i>nested</i> record projection.</summary>
public sealed record Squad(PlayerInfo Leader, string Banner);

/// <summary>
/// A rich event carrying every marshaller-eligible kind — Guid, enum, the four scalars + string, an array, and
/// a nested DTO — plus a scalar (<see cref="Distance"/>) the Where filters on. Used to prove a whole-event
/// RunLocal push and each non-scalar projection round-trip with field-level fidelity.
/// </summary>
public sealed record EncounterEvent(
    Guid EncounterId,
    GamePhase Phase,
    bool Boss,
    int Distance,
    long Score,
    double Multiplier,
    string Zone,
    int[] MonsterIds,
    PlayerInfo Player);

/// <summary>
/// Rich-type coverage for remote <c>RunLocal</c>: a whole-event push of an event carrying Guid/enum/array/nested
/// DTO, and projections to each non-scalar kind (Guid, enum, list, nested DTO, and a constructed
/// <c>new Dto(...)</c>). Every case asserts the value survives the full server-filter -&gt; server-project/encode
/// -&gt; wire -&gt; decode path with field-level fidelity, over BOTH decode paths (the runtime fallback and the
/// generated reflection-free decoder), and that filtering stays server-side.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private static readonly Guid SampleId = new("0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9");

    private static EncounterEvent Matching => new(
        SampleId, GamePhase.Victory, Boss: true, Distance: 3, Score: 9_000_000_000L, Multiplier: 1.25,
        Zone: "crypt", MonsterIds: [3, 1, 4, 1, 5], Player: new PlayerInfo("hero", 7));

    private static EncounterEvent Filtered => Matching with { Distance = 99 };

    private const string Prelude = """
        using System;
        using DotBoxD.Plugins.Runtime;
        using Ev = global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;
        namespace ChainSample;
        """;

    private const string WholeEventRichSource = Prelude + """
        public static class WholeEventRichUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4).RunLocal((e, ctx) => { });
        }
        """;

    private const string GuidProjectionSource = Prelude + """
        public static class GuidProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => e.EncounterId).RunLocal((id, ctx) => { });
        }
        """;

    private const string EnumProjectionSource = Prelude + """
        public static class EnumProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => e.Phase).RunLocal((phase, ctx) => { });
        }
        """;

    private const string ArrayProjectionSource = Prelude + """
        public static class ArrayProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => e.MonsterIds).RunLocal((ids, ctx) => { });
        }
        """;

    private const string NestedDtoProjectionSource = Prelude + """
        public static class NestedDtoProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => e.Player).RunLocal((player, ctx) => { });
        }
        """;

    private const string NewDtoProjectionSource = Prelude + """
        public static class NewDtoProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new Ev.EncounterTicket(e.EncounterId, e.Zone)).RunLocal((ticket, ctx) => { });
        }
        """;

    private const string NestedNewDtoProjectionSource = Prelude + """
        public static class NestedNewDtoProjectionUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new Ev.Squad(e.Player, e.Zone)).RunLocal((squad, ctx) => { });
        }
        """;

    [Fact]
    public async Task Whole_event_with_rich_fields_round_trips_with_field_fidelity()
    {
        var payload = await PushFirstMatching(WholeEventRichSource, Matching, Filtered);

        AssertEncounter(DecodeReflective<EncounterEvent>(payload));
        AssertEncounter(DecodeGenerated<EncounterEvent>(WholeEventRichSource, payload));
    }

    [Fact]
    public async Task Guid_projection_round_trips_over_both_decode_paths()
    {
        var payload = await PushFirstMatching(GuidProjectionSource, Matching, Filtered);

        Assert.Equal(SampleId, DecodeReflective<Guid>(payload));
        Assert.Equal(SampleId, DecodeGenerated<Guid>(GuidProjectionSource, payload));
    }

    [Fact]
    public async Task Enum_projection_round_trips_over_both_decode_paths()
    {
        var payload = await PushFirstMatching(EnumProjectionSource, Matching, Filtered);

        Assert.Equal(GamePhase.Victory, DecodeReflective<GamePhase>(payload));
        Assert.Equal(GamePhase.Victory, DecodeGenerated<GamePhase>(EnumProjectionSource, payload));
    }

    [Fact]
    public async Task Array_projection_preserves_elements_and_order()
    {
        var payload = await PushFirstMatching(ArrayProjectionSource, Matching, Filtered);

        Assert.Equal(new[] { 3, 1, 4, 1, 5 }, DecodeReflective<int[]>(payload));
        Assert.Equal(new[] { 3, 1, 4, 1, 5 }, DecodeGenerated<int[]>(ArrayProjectionSource, payload));
    }

    [Fact]
    public async Task Nested_dto_projection_round_trips_its_fields()
    {
        var payload = await PushFirstMatching(NestedDtoProjectionSource, Matching, Filtered);

        Assert.Equal(new PlayerInfo("hero", 7), DecodeReflective<PlayerInfo>(payload));
        Assert.Equal(new PlayerInfo("hero", 7), DecodeGenerated<PlayerInfo>(NestedDtoProjectionSource, payload));
    }

    [Fact]
    public async Task Constructed_new_dto_projection_round_trips_with_field_fidelity()
    {
        var payload = await PushFirstMatching(NewDtoProjectionSource, Matching, Filtered);

        var expected = new EncounterTicket(SampleId, "crypt");
        Assert.Equal(expected, DecodeReflective<EncounterTicket>(payload));
        Assert.Equal(expected, DecodeGenerated<EncounterTicket>(NewDtoProjectionSource, payload));
    }

    [Fact]
    public async Task Constructed_nested_dto_projection_round_trips_the_inner_record()
    {
        // new Squad(e.Player, e.Zone): the projected record's first field is itself a record (PlayerInfo), so
        // record.new nests a record value — the inner DTO's fields must survive the round-trip too.
        var payload = await PushFirstMatching(NestedNewDtoProjectionSource, Matching, Filtered);

        var expected = new Squad(new PlayerInfo("hero", 7), "crypt");
        Assert.Equal(expected, DecodeReflective<Squad>(payload));
        Assert.Equal(expected, DecodeGenerated<Squad>(NestedNewDtoProjectionSource, payload));
    }

    [Fact]
    public async Task A_non_matching_rich_event_never_crosses_the_wire()
    {
        // Filtering runs server-side BEFORE any push, even for an event that carries non-scalar fields: an event
        // failing the Where produces no payload at all.
        var package = LowerToPackage(WholeEventRichSource);
        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: ChainPolicy());
        var kernel = await server.InstallAsync(package);

        var pushed = new List<byte[]>();
        RemoteLocalPush push = (_, payload, _) =>
        {
            pushed.Add(payload.ToArray());
            return ValueTask.CompletedTask;
        };
        server.Hooks.On<EncounterEvent>().UseProjecting(kernel, "sub-none", push);

        await server.Hooks.PublishAsync(Filtered);   // 99 > 4 → filtered server-side
        Assert.Empty(pushed);
    }

    private static void AssertEncounter(EncounterEvent received)
    {
        Assert.Equal(SampleId, received.EncounterId);          // Guid survives
        Assert.Equal(GamePhase.Victory, received.Phase);       // enum value survives
        Assert.True(received.Boss);
        Assert.Equal(3, received.Distance);
        Assert.Equal(9_000_000_000L, received.Score);          // > int range → exercises I64
        Assert.Equal(1.25, received.Multiplier);
        Assert.Equal("crypt", received.Zone);
        Assert.Equal(new[] { 3, 1, 4, 1, 5 }, received.MonsterIds);  // array elements + order survive
        Assert.Equal(new PlayerInfo("hero", 7), received.Player);    // nested record fields survive
    }

    // Server-side: filter + project run in the sandbox; capture the single payload pushed for the matching event
    // and assert the non-matching event was filtered before any IPC. This is the real transport the plugin sees.
    private static async Task<byte[]> PushFirstMatching<TEvent>(string source, TEvent matching, TEvent filtered)
    {
        var package = LowerToPackage(source);
        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: ChainPolicy());
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

        Assert.Single(pushed);   // exactly one of two events crossed the wire (filter ran server-side)
        return pushed[0];
    }

    // The runtime fallback decode path (RemoteLocalHandlerRegistry's 2-arg Register): wire -> CLR without a
    // generated decoder.
    private static T DecodeReflective<T>(byte[] payload)
    {
        var wire = KernelRpcBinaryCodec.DecodeValue(payload);
        var sandbox = KernelRpcValueConverter.ToSandboxValue(wire, KernelRpcMarshaller.SandboxTypeOf(typeof(T)));
        return (T)KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(T))!;
    }

    // The generated reflection-free decode path: invoke the package's emitted ReadProjectedPayload(bytes)
    // straight on the pushed payload, exactly as the interceptor wires it for the native delegate.
    private static T DecodeGenerated<T>(string source, byte[] payload)
        => (T)DecodeGeneratedObject(source, payload);

    private static object DecodeGeneratedObject(string source, byte[] payload)
    {
        var assembly = Compile(source, enableInterceptors: true);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.Name.EndsWith("PluginPackage", StringComparison.Ordinal));
        var readProjected = packageType.GetMethod("ReadProjectedPayload", BindingFlags.Public | BindingFlags.Static)!;
        return readProjected.Invoke(null, [new ReadOnlyMemory<byte>(payload)])!;
    }
}
