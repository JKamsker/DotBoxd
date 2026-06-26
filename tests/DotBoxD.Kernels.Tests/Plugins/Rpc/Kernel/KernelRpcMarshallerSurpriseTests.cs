using System.Collections;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcMarshallerSurpriseTests
{
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

    private enum IntBackedEnum
    {
        One = 1
    }

    private enum LongBackedEnum : long
    {
        One = 1
    }
}
