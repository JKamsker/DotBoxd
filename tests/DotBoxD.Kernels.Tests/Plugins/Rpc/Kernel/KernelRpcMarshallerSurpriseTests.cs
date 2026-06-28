using System.Collections;
using System.Collections.Immutable;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed partial class KernelRpcMarshallerSurpriseTests
{
    // An IEnumerable<T> collection that is not a recognized list/map (e.g. ImmutableArray<T>) exposes only
    // scalar getters (Length/IsEmpty/...) and would otherwise be mis-shaped as a metadata-only record that
    // silently drops its elements. The runtime DTO-shape detection must fail closed (throw) on it, mirroring
    // the analyzer's generic-enumerable exclusion, instead of marshalling it as a record.
    [Fact]
    public void ToSandboxValue_rejects_immutable_array_collection_instead_of_shaping_a_record()
    {
        var value = ImmutableArray.Create(1, 2, 3);

        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.ToSandboxValue(value, typeof(ImmutableArray<int>)));

        Assert.Contains("ImmutableArray", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromSandboxValue_rejects_unreconstructable_get_only_wire_field()
    {
        var sandbox = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(7), SandboxValue.FromInt32(42)]);

        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(GetOnlyTailDto)));

        Assert.Contains("Computed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromSandboxValue_accepts_get_only_wire_field_that_recomputes_from_assigned_fields()
    {
        var sandbox = SandboxValue.FromRecord(
            [SandboxValue.FromInt32(3), SandboxValue.FromInt32(4), SandboxValue.FromInt32(7)]);

        var dto = Assert.IsType<ComputedTailDto>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(ComputedTailDto)));

        Assert.Equal(3, dto.X);
        Assert.Equal(4, dto.Y);
        Assert.Equal(7, dto.Sum);
    }

    [Fact]
    public void ToSandboxValue_writes_readonly_dictionary_implementations()
    {
        IReadOnlyDictionary<string, int> scores = new ReadOnlyScoreMap(
            new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });

        var sandbox = KernelRpcMarshaller.ToSandboxValue(scores, typeof(IReadOnlyDictionary<string, int>));

        var map = Assert.IsType<MapValue>(sandbox);
        Assert.Equal(SandboxType.Map(SandboxType.String, SandboxType.I32), map.Type);
        Assert.Equal(2, map.Values.Count);
        Assert.Equal(SandboxValue.FromInt32(1), map.Values[SandboxValue.FromString("a")]);
        Assert.Equal(SandboxValue.FromInt32(2), map.Values[SandboxValue.FromString("b")]);
    }

    [Fact]
    public void SandboxTypeOf_ignores_public_setter_private_getter_properties()
        => Assert.Equal(
            SandboxType.Record([SandboxType.I32]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(PublicSetterPrivateGetterDto)));

    [Fact]
    public void ToSandboxValue_ignores_public_setter_private_getter_properties()
    {
        var sandbox = KernelRpcMarshaller.ToSandboxValue(
            new PublicSetterPrivateGetterDto(8, "hidden"),
            typeof(PublicSetterPrivateGetterDto));

        var record = Assert.IsType<RecordValue>(sandbox);
        Assert.Equal([SandboxValue.FromInt32(8)], record.Fields);
    }

    [Fact]
    public void FromSandboxValue_rejects_private_setter_dto_properties()
    {
        var sandbox = SandboxValue.FromRecord([SandboxValue.FromInt32(9)]);

        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(PrivateSetterDto)));

        Assert.Contains("private", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromSandboxValue_rejects_enum_values_with_wrong_integer_width()
    {
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt64(1), typeof(IntBackedEnum)));
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt32(1), typeof(LongBackedEnum)));
    }

    [Fact]
    public void FromSandboxValue_rejects_finite_double_values_that_overflow_float()
    {
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(
                SandboxValue.FromDouble(double.MaxValue),
                typeof(float)));
    }

    [Fact]
    public void FromKernelRpcValue_rejects_finite_double_values_that_overflow_float()
    {
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(
                KernelRpcValue.Double(double.MaxValue),
                typeof(float)));
    }

    [Fact]
    public void FromKernelRpcValue_rejects_duplicate_map_keys()
    {
        var wire = KernelRpcValue.Map(
        [
            KernelRpcValue.String("same"),
            KernelRpcValue.Int32(1),
            KernelRpcValue.String("same"),
            KernelRpcValue.Int32(2)
        ]);

        var ex = Assert.Throws<FormatException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(
                wire,
                typeof(Dictionary<string, int>)));

        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromSandboxValue_rejects_narrow_enum_values_outside_underlying_range()
    {
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt32(300), typeof(ByteBackedEnum)));
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(SandboxValue.FromInt32(70000), typeof(ShortBackedEnum)));
    }

    [Fact]
    public void FromKernelRpcValue_rejects_narrow_enum_values_outside_underlying_range()
    {
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(KernelRpcValue.Int32(300), typeof(ByteBackedEnum)));
        Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(KernelRpcValue.Int32(70000), typeof(ShortBackedEnum)));
    }

    [Fact]
    public void FromSandboxValue_rejects_float_overflow_inside_constructor_dto()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(
                SandboxValue.FromRecord([SandboxValue.FromDouble(double.MaxValue)]),
                typeof(ConstructorFloatDto)));

    [Fact]
    public void FromSandboxValue_rejects_float_overflow_inside_setter_dto()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromSandboxValue(
                SandboxValue.FromRecord([SandboxValue.FromDouble(double.MaxValue)]),
                typeof(SetterFloatDto)));

    [Fact]
    public void FromKernelRpcValue_rejects_float_overflow_inside_constructor_dto()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(
                KernelRpcValue.Record([KernelRpcValue.Double(double.MaxValue)]),
                typeof(ConstructorFloatDto)));

    [Fact]
    public void FromKernelRpcValue_rejects_float_overflow_inside_setter_dto()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(
                KernelRpcValue.Record([KernelRpcValue.Double(double.MaxValue)]),
                typeof(SetterFloatDto)));

    [Fact]
    public void FromKernelRpcValue_rejects_narrow_enum_overflow_inside_constructor_dto()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(
                KernelRpcValue.Record([KernelRpcValue.Int32(300)]),
                typeof(ConstructorEnumDto)));

    [Fact]
    public void FromKernelRpcValue_rejects_narrow_enum_overflow_inside_setter_dto()
        => Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.FromKernelRpcValue(
                KernelRpcValue.Record([KernelRpcValue.Int32(300)]),
                typeof(SetterEnumDto)));

    [Fact]
    public void SandboxTypeOf_rejects_cancellation_token_before_dto_reflection()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => KernelRpcMarshaller.SandboxTypeOf(typeof(CancellationToken)));

        Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nests beyond", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromKernelRpcValue_uses_constructor_with_unmatched_optional_parameter()
    {
        var value = KernelRpcValue.Record([KernelRpcValue.Int32(3), KernelRpcValue.Int32(9)]);

        var dto = Assert.IsType<OptionalProfileDto>(
            KernelRpcMarshaller.FromKernelRpcValue(value, typeof(OptionalProfileDto)));

        Assert.Equal(3, dto.Health);
        Assert.Equal(9, dto.Rank);
    }

    private sealed class GetOnlyTailDto
    {
        public int Id { get; set; }

        public int Computed { get; } = 5;
    }

    private sealed class ComputedTailDto
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int Sum => X + Y;
    }

    private sealed class ReadOnlyScoreMap(IReadOnlyDictionary<string, int> inner)
        : IReadOnlyDictionary<string, int>
    {
        public int this[string key] => inner[key];

        public IEnumerable<string> Keys => inner.Keys;

        public IEnumerable<int> Values => inner.Values;

        public int Count => inner.Count;

        public bool ContainsKey(string key) => inner.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => inner.GetEnumerator();

        public bool TryGetValue(string key, out int value) => inner.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class PublicSetterPrivateGetterDto(int id, string secret)
    {
        public int Id { get; set; } = id;

        public string Secret { private get; set; } = secret;
    }

    private sealed class PrivateSetterDto
    {
        public int Id { get; private set; }
    }

    private sealed class ConstructorFloatDto(float value)
    {
        public float Value { get; } = value;
    }

    private sealed class SetterFloatDto
    {
        public float Value { get; set; }
    }

    private sealed class ConstructorEnumDto(ByteBackedEnum value)
    {
        public ByteBackedEnum Value { get; } = value;
    }

    private sealed class SetterEnumDto
    {
        public ByteBackedEnum Value { get; set; }
    }

    private sealed class OptionalProfileDto
    {
        public OptionalProfileDto(int health, bool normalize = true)
        {
            Health = normalize ? health : -health;
        }

        public int Health { get; }

        public int Rank { get; set; }
    }

    private enum IntBackedEnum
    {
        One = 1
    }

    private enum LongBackedEnum : long
    {
        One = 1
    }

    private enum ByteBackedEnum : byte
    {
        Zero = 0,
        FortyFour = 44
    }

    private enum ShortBackedEnum : short
    {
        Zero = 0
    }
}
