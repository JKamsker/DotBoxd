using System.Collections;
using System.Collections.Immutable;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcMarshallerSurpriseTests
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
}
