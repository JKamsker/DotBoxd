using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks.RemoteLocal;

public sealed class RemoteLocalResultShapeSurpriseTests
{
    [Fact]
    public async Task DispatchResultAsync_encodes_mixed_property_and_field_results()
    {
        var registry = new RemoteLocalHandlerRegistry();
        registry.RegisterResult<DamageContext, MixedDamageResult>(
            "mixed-result",
            (context, _) => new MixedDamageResult
            {
                Success = true,
                Reason = "ok",
                Damage = context.Damage * 2
            });

        var response = await registry.DispatchResultAsync(
            "mixed-result",
            EncodeProjected(new DamageContext(21)),
            new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));

        var result = KernelRpcBinaryCodec.DecodeValue(response);
        Assert.Equal(KernelRpcValueKind.Record, result.Kind);
        Assert.Equal(3, result.ItemCount);
        Assert.True(result.Items[0].BoolValue);
        Assert.Equal("ok", result.Items[1].TextValue);
        Assert.Equal(42, result.Items[2].Int32Value);
    }

    private static byte[] EncodeProjected<T>(T value)
    {
        var sandboxValue = KernelRpcMarshaller.ToSandboxValue(value, typeof(T));
        return KernelRpcBinaryCodec.EncodeValue(sandboxValue);
    }

    private sealed record DamageContext(int Damage);

    private struct MixedDamageResult : IHookResult
    {
        public bool Success { get; init; }

        public string? Reason { get; init; }

        public int Damage;
    }
}
