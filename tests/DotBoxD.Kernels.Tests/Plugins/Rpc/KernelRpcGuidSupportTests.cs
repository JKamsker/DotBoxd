using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Guid is a first-class scalar: a dedicated 16-byte wire kind (<see cref="KernelRpcValueKind.Guid"/>) and a
/// dedicated sandbox scalar (<see cref="GuidValue"/> / <see cref="SandboxType.Guid"/>). These tests pin the
/// round-trip at each layer the lowered RunLocal push relies on — binary codec, sandbox/wire converter, the
/// reflective marshaller — plus the full CLR -> sandbox -> wire -> bytes -> ... -> CLR pipeline.
/// </summary>
public sealed class KernelRpcGuidSupportTests
{
    private static readonly Guid Sample = new("0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9");

    [Fact]
    public void Guid_value_survives_a_binary_round_trip_as_16_bytes()
    {
        var payload = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Guid(Sample));

        // 1 kind byte + 16 raw Guid bytes, no length prefix (fixed-width scalar).
        Assert.Equal(17, payload.Length);
        Assert.Equal((byte)KernelRpcValueKind.Guid, payload[0]);

        var value = KernelRpcBinaryCodec.DecodeValue(payload);
        value.RequireKind(KernelRpcValueKind.Guid);
        Assert.Equal(Sample, value.GuidValue);
    }

    [Fact]
    public void GuidValue_accessor_rejects_a_non_guid_kind()
        => Assert.Throws<NotSupportedException>(() => _ = KernelRpcValue.Int32(1).GuidValue);

    [Fact]
    public void Converter_round_trips_a_guid_between_sandbox_and_wire()
    {
        var sandbox = SandboxValue.FromGuid(Sample);

        var wire = KernelRpcValueConverter.FromSandboxValue(sandbox);
        Assert.Equal(KernelRpcValueKind.Guid, wire.Kind);
        Assert.Equal(Sample, wire.GuidValue);

        var back = KernelRpcValueConverter.ToSandboxValue(wire, SandboxType.Guid);
        Assert.Equal(sandbox, back);
        Assert.Equal(Sample, Assert.IsType<GuidValue>(back).Value);
    }

    [Fact]
    public void Converter_rejects_a_non_guid_wire_value_for_a_guid_type()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcValueConverter.ToSandboxValue(KernelRpcValue.String("x"), SandboxType.Guid));

    [Fact]
    public void Marshaller_round_trips_a_clr_guid()
    {
        Assert.Equal(SandboxType.Guid, KernelRpcMarshaller.SandboxTypeOf(typeof(Guid)));

        var sandbox = KernelRpcMarshaller.ToSandboxValue(Sample, typeof(Guid));
        Assert.Equal(Sample, Assert.IsType<GuidValue>(sandbox).Value);

        var clr = KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(Guid));
        Assert.Equal(Sample, Assert.IsType<Guid>(clr));
    }

    [Fact]
    public void Marshaller_round_trips_a_record_carrying_a_guid_field()
    {
        var dto = new GuidCarrier(Sample, "caster-7");

        var sandbox = KernelRpcMarshaller.ToSandboxValue(dto, typeof(GuidCarrier));
        var clr = (GuidCarrier)KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(GuidCarrier))!;

        Assert.Equal(Sample, clr.Id);
        Assert.Equal("caster-7", clr.Name);
    }

    [Fact]
    public void Full_stack_round_trips_a_guid_clr_to_bytes_and_back()
    {
        // CLR -> sandbox -> wire -> bytes -> wire -> sandbox -> CLR: the exact path a whole-event/projection
        // push takes a Guid through (host encode, IPC transport, plugin decode).
        var encoded = KernelRpcBinaryCodec.EncodeValue(
            KernelRpcValueConverter.FromSandboxValue(KernelRpcMarshaller.ToSandboxValue(Sample, typeof(Guid))));

        var decodedSandbox = KernelRpcValueConverter.ToSandboxValue(
            KernelRpcBinaryCodec.DecodeValue(encoded),
            SandboxType.Guid);
        var clr = (Guid)KernelRpcMarshaller.FromSandboxValue(decodedSandbox, typeof(Guid))!;

        Assert.Equal(Sample, clr);
    }

    [Fact]
    public void Marshaller_rejects_a_guid_keyed_map_type()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(Dictionary<Guid, string>)));

    [Fact]
    public void Marshaller_rejects_a_guid_keyed_map_value()
    {
        // The kernel verifier only accepts a fixed set of scalar map keys (Guid is not one). The value-marshalling
        // map branch must reject a Dictionary<Guid, V> exactly as the type-level SandboxTypeOf guard does, rather
        // than producing a Map<Guid, V> that later fails IsKnown at install.
        var map = new Dictionary<Guid, string> { [Sample] = "caster-7" };

        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(map, typeof(Dictionary<Guid, string>)));
    }

    private sealed record GuidCarrier(Guid Id, string Name);
}
