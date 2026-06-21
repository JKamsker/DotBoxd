namespace DotBoxD.Kernels.Benchmarks.Ipc.RunLocal;

using System.Buffers;
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;

/// <summary>
/// One representative remote <c>RunLocal</c> projection type, wired end-to-end through the push path
/// (server-side encode → IPC bytes → client-side decode → native delegate invoke) so the benchmark and the
/// probe can measure the encode-half and decode-half allocations per phase of issue #60.
/// </summary>
public enum RunLocalPushCase
{
    /// <summary>Scalar <c>int</c> projection (a <c>Select(e =&gt; e.MonsterId)</c> shape).</summary>
    Int32,

    /// <summary>Scalar <c>string</c> projection.</summary>
    String,

    /// <summary>An enum projection (marshalled through its underlying integer).</summary>
    Enum,

    /// <summary><c>List&lt;int&gt;</c> projection.</summary>
    ListInt32,

    /// <summary>A small DTO record projection (<c>int</c> + <c>string</c>).</summary>
    Dto,

    /// <summary>A terminal anonymous-object projection with the same wire shape as <see cref="Dto"/>.</summary>
    AnonymousDto,

    /// <summary>A whole-event record push (no <c>Select</c>) — several scalar fields.</summary>
    WholeEvent,
}

/// <summary>The reaction an enum projection can carry; mirrors a plausible plugin projection.</summary>
internal enum RunLocalReaction
{
    Calm,
    Alert,
    Flee,
}

/// <summary>A small DTO record projection: the wire-eligible record shape.</summary>
internal sealed record RunLocalHit(int MonsterId, string Name);

/// <summary>A whole-event record push shape: several scalar fields, the kind of event a whole-event chain emits.</summary>
internal sealed record RunLocalMonsterDamaged(int MonsterId, int Damage, long Timestamp, bool Critical);

/// <summary>
/// Holds the prebuilt per-case state — the source <see cref="SandboxValue"/> payload, the pre-encoded wire
/// bytes, and a <see cref="RemoteLocalHandlerRegistry"/> with the native terminal already registered — so the
/// measured methods do nothing but the encode/decode work under test. A side-effecting checksum keeps the
/// decoded value live so the JIT cannot elide the decode.
/// </summary>
internal sealed class RunLocalPushScenario
{
    private const string SubscriptionId = "runlocal-bench";
    private const string GeneratedSubscriptionId = "runlocal-bench-gen";

    private readonly RemoteLocalHandlerRegistry _registry = new();
    private readonly HookContext _context = new(new InMemoryPluginMessageSink(), CancellationToken.None);

    // Reused across iterations, mirroring the pooled writer the production push path rents: the buffer persists,
    // so the encode-half measures the genuine codec work, not a fresh array per call.
    private readonly ArrayBufferWriter<byte> _encodeWriter = new(256);

    private RunLocalPushScenario(SandboxValue payload)
    {
        Payload = payload;
        Encoded = Encode(payload);
    }

    public SandboxValue Payload { get; }

    public byte[] Encoded { get; }

    public long Checksum { get; private set; }

    public static RunLocalPushScenario Create(RunLocalPushCase scenario)
    {
        var instance = scenario switch
        {
            RunLocalPushCase.Int32 => Build(SandboxValue.FromInt32(73)),
            RunLocalPushCase.String => Build(SandboxValue.FromString("monster-7")),
            RunLocalPushCase.Enum => Build(SandboxValue.FromInt32((int)RunLocalReaction.Flee)),
            RunLocalPushCase.ListInt32 => Build(SandboxValue.FromList(
                [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2), SandboxValue.FromInt32(3)],
                SandboxType.I32)),
            RunLocalPushCase.Dto => Build(SandboxValue.FromRecord(
                [SandboxValue.FromInt32(73), SandboxValue.FromString("ogre")])),
            RunLocalPushCase.AnonymousDto => BuildAnonymous(
                new { MonsterId = 73, Name = "crypt" },
                SandboxValue.FromRecord([SandboxValue.FromInt32(73), SandboxValue.FromString("crypt")])),
            RunLocalPushCase.WholeEvent => Build(SandboxValue.FromRecord(
            [
                SandboxValue.FromInt32(73),
                SandboxValue.FromInt32(250),
                SandboxValue.FromInt64(1_700_000_000_000),
                SandboxValue.FromBool(true),
            ])),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        if (scenario != RunLocalPushCase.AnonymousDto)
        {
            instance.Register(scenario);
        }

        return instance;
    }

    /// <summary>
    /// The server-side encode half exactly as the push path runs it: encode a sandbox value straight into a
    /// reused buffer (no per-call array). Returns the byte count so nothing is allocated to observe.
    /// </summary>
    public int Encode()
    {
        _encodeWriter.ResetWrittenCount();
        KernelRpcBinaryCodec.EncodeValue(Payload, _encodeWriter);
        return _encodeWriter.WrittenCount;
    }

    /// <summary>The client-side decode half via the runtime fallback decoder.</summary>
    public ValueTask DecodeInvokeAsync() => _registry.DispatchAsync(SubscriptionId, Encoded, _context);

    /// <summary>
    /// The client-side decode half via the generated reflection-free decoder — the path the plugin generator
    /// emits for wire-eligible projected types: typed reads straight off the binary payload.
    /// </summary>
    public ValueTask DecodeInvokeGeneratedAsync() => _registry.DispatchAsync(GeneratedSubscriptionId, Encoded, _context);

    /// <summary>The full push path measured together: encode into the reused buffer, then decode + invoke.</summary>
    public ValueTask RoundTripAsync()
    {
        _encodeWriter.ResetWrittenCount();
        KernelRpcBinaryCodec.EncodeValue(Payload, _encodeWriter);
        return _registry.DispatchAsync(SubscriptionId, _encodeWriter.WrittenMemory, _context);
    }

    private static RunLocalPushScenario Build(SandboxValue payload) => new(payload);

    private static RunLocalPushScenario BuildAnonymous<TProjected>(TProjected sample, SandboxValue payload)
        where TProjected : class
    {
        var instance = Build(payload);
        instance.RegisterAnonymous(sample);
        return instance;
    }

    private static byte[] Encode(SandboxValue payload) => KernelRpcBinaryCodec.EncodeValue(payload);

    // Registers the native terminal twice per case: once through the runtime fallback (the 2-arg overload) and
    // once with a generated-shape decoder (the 3-arg path), so the probe/benchmark can compare both decode halves. The
    // generated decoders read straight off KernelRpcValue's typed fields — byte-for-byte what
    // RpcKernelValueConversionEmitter emits — so they faithfully model the generator's output.
    private void Register(RunLocalPushCase scenario)
    {
        switch (scenario)
        {
            case RunLocalPushCase.Int32:
                _registry.Register<int>(SubscriptionId, (value, _) => Accumulate(value));
                _registry.Register<int>(GeneratedSubscriptionId, (value, _) => Accumulate(value), static payload => ReadInt32Payload(payload));
                break;
            case RunLocalPushCase.String:
                _registry.Register<string>(SubscriptionId, (value, _) => Accumulate(value.Length));
                _registry.Register<string>(GeneratedSubscriptionId, (value, _) => Accumulate(value.Length), static payload => ReadStringPayload(payload));
                break;
            case RunLocalPushCase.Enum:
                _registry.Register<RunLocalReaction>(SubscriptionId, (value, _) => Accumulate((int)value));
                _registry.Register<RunLocalReaction>(GeneratedSubscriptionId, (value, _) => Accumulate((int)value), static payload => ReadReactionPayload(payload));
                break;
            case RunLocalPushCase.ListInt32:
                _registry.Register<List<int>>(SubscriptionId, (value, _) => Accumulate(value.Count));
                _registry.Register<List<int>>(GeneratedSubscriptionId, (value, _) => Accumulate(value.Count), static payload => ReadInt32ListPayload(payload));
                break;
            case RunLocalPushCase.Dto:
                _registry.Register<RunLocalHit>(SubscriptionId, (value, _) => Accumulate(value.MonsterId));
                _registry.Register<RunLocalHit>(GeneratedSubscriptionId, (value, _) => Accumulate(value.MonsterId),
                    static payload => ReadHitPayload(payload));
                break;
            case RunLocalPushCase.AnonymousDto:
                throw new InvalidOperationException("Anonymous projections are registered through their inferred type.");
            case RunLocalPushCase.WholeEvent:
                _registry.Register<RunLocalMonsterDamaged>(SubscriptionId, (value, _) => Accumulate(value.Damage));
                _registry.Register<RunLocalMonsterDamaged>(GeneratedSubscriptionId, (value, _) => Accumulate(value.Damage),
                    static payload => ReadMonsterDamagedPayload(payload));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    private void RegisterAnonymous<TProjected>(TProjected sample)
        where TProjected : class
    {
        _registry.Register<TProjected>(SubscriptionId, (value, _) => Accumulate(value.GetHashCode()));
        _registry.Register<TProjected>(
            GeneratedSubscriptionId,
            (value, _) => Accumulate(value.GetHashCode()),
            static payload => ReadAnonymousPayload<TProjected>(payload));
        GC.KeepAlive(sample);
    }

    private static int ReadInt32Payload(ReadOnlyMemory<byte> payload)
    {
        var reader = new KernelRpcPayloadReader(payload.Span);
        var result = reader.ReadInt32();
        reader.EnsureConsumed();
        return result;
    }

    private static string ReadStringPayload(ReadOnlyMemory<byte> payload)
    {
        var reader = new KernelRpcPayloadReader(payload.Span);
        var result = reader.ReadString();
        reader.EnsureConsumed();
        return result;
    }

    private static RunLocalReaction ReadReactionPayload(ReadOnlyMemory<byte> payload)
    {
        var reader = new KernelRpcPayloadReader(payload.Span);
        var result = (RunLocalReaction)reader.ReadInt32();
        reader.EnsureConsumed();
        return result;
    }

    private static List<int> ReadInt32ListPayload(ReadOnlyMemory<byte> payload)
    {
        var reader = new KernelRpcPayloadReader(payload.Span);
        var count = reader.ReadListHeader();
        var result = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(reader.ReadInt32());
        }

        reader.EnsureConsumed();
        return result;
    }

    private static RunLocalHit ReadHitPayload(ReadOnlyMemory<byte> payload)
    {
        var reader = new KernelRpcPayloadReader(payload.Span);
        RequireRecordFields(ref reader, 2);
        var result = new RunLocalHit(reader.ReadInt32(), reader.ReadString());
        reader.EnsureConsumed();
        return result;
    }

    private static RunLocalMonsterDamaged ReadMonsterDamagedPayload(ReadOnlyMemory<byte> payload)
    {
        var reader = new KernelRpcPayloadReader(payload.Span);
        RequireRecordFields(ref reader, 4);
        var result = new RunLocalMonsterDamaged(
            reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64(), reader.ReadBool());
        reader.EnsureConsumed();
        return result;
    }

    private static TProjected ReadAnonymousPayload<TProjected>(ReadOnlyMemory<byte> payload)
        where TProjected : class
    {
        var reader = new KernelRpcPayloadReader(payload.Span);
        RequireRecordFields(ref reader, 2);
        var result = (TProjected)(object)new { MonsterId = reader.ReadInt32(), Name = reader.ReadString() };
        reader.EnsureConsumed();
        return result;
    }

    private static void RequireRecordFields(ref KernelRpcPayloadReader reader, int expected)
    {
        if (reader.ReadRecordHeader() != expected)
        {
            throw new NotSupportedException("Benchmark projection field count changed.");
        }
    }

    private ValueTask Accumulate(long value)
    {
        Checksum += value;
        return ValueTask.CompletedTask;
    }
}
