using System.Reflection;
using DotBoxD.Plugins;
using RpcNames = DotBoxD.Plugins.Analyzer.Analysis.Rpc.DotBoxDRpcValueNames;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

/// <summary>
/// Pins the <c>KernelRpcValue</c> transport-family names the plugin analyzer bakes into generated proxies
/// and dispatchers. The analyzer emits these as raw strings (it cannot reference <c>DotBoxD.Plugins</c>),
/// so a rename or namespace move on the runtime type would silently produce code that fails to compile in
/// consumer projects. Each constant is anchored to the real symbol via <c>typeof</c>/<c>nameof</c>: a move
/// breaks the anchor (compile error) and a rename breaks the value assertion (red test). The key-set
/// assertions also fail if a constant is added to <see cref="RpcNames"/> without a matching anchor here.
/// </summary>
public sealed class DotBoxDRpcValueNameContractTests
{
    [Fact]
    public void Type_constants_match_runtime_full_names()
    {
        Assert.Equal(RpcNames.GlobalKernelRpcValue, RpcNames.GlobalPrefix + typeof(KernelRpcValue).FullName);
        Assert.Equal(RpcNames.GlobalKernelRpcValueKind, RpcNames.GlobalPrefix + typeof(KernelRpcValueKind).FullName);
        Assert.Equal(RpcNames.GlobalKernelRpcBinaryCodec, RpcNames.GlobalPrefix + typeof(KernelRpcBinaryCodec).FullName);
    }

    [Fact]
    public void KernelRpcValue_member_constants_mirror_runtime_members()
    {
        var expected = new Dictionary<string, string>
        {
            ["Unit"] = nameof(KernelRpcValue.Unit),
            ["Bool"] = nameof(KernelRpcValue.Bool),
            ["Int32"] = nameof(KernelRpcValue.Int32),
            ["Int64"] = nameof(KernelRpcValue.Int64),
            ["Double"] = nameof(KernelRpcValue.Double),
            ["String"] = nameof(KernelRpcValue.String),
            ["Guid"] = nameof(KernelRpcValue.Guid),
            ["List"] = nameof(KernelRpcValue.List),
            ["Record"] = nameof(KernelRpcValue.Record),
            ["Map"] = nameof(KernelRpcValue.Map),
            ["GetItem"] = nameof(KernelRpcValue.GetItem),
            ["RequireKind"] = nameof(KernelRpcValue.RequireKind),
            ["Kind"] = nameof(KernelRpcValue.Kind),
            ["ItemCount"] = nameof(KernelRpcValue.ItemCount),
            ["BoolValue"] = nameof(KernelRpcValue.BoolValue),
            ["Int32Value"] = nameof(KernelRpcValue.Int32Value),
            ["Int64Value"] = nameof(KernelRpcValue.Int64Value),
            ["DoubleValue"] = nameof(KernelRpcValue.DoubleValue),
            ["TextValue"] = nameof(KernelRpcValue.TextValue),
            ["GuidValue"] = nameof(KernelRpcValue.GuidValue),
        };

        AssertConstantsMirror(typeof(RpcNames.KernelRpcValue), expected);
    }

    [Fact]
    public void KernelRpcValueKind_member_constants_mirror_runtime_members()
    {
        var expected = new Dictionary<string, string>
        {
            ["Unit"] = nameof(KernelRpcValueKind.Unit),
            ["List"] = nameof(KernelRpcValueKind.List),
            ["Record"] = nameof(KernelRpcValueKind.Record),
            ["Map"] = nameof(KernelRpcValueKind.Map),
        };

        AssertConstantsMirror(typeof(RpcNames.KernelRpcValueKind), expected);
    }

    [Fact]
    public void KernelRpcBinaryCodec_member_constants_mirror_runtime_members()
    {
        var expected = new Dictionary<string, string>
        {
            ["EncodeArguments"] = nameof(KernelRpcBinaryCodec.EncodeArguments),
            ["DecodeValue"] = nameof(KernelRpcBinaryCodec.DecodeValue),
        };

        AssertConstantsMirror(typeof(RpcNames.KernelRpcBinaryCodec), expected);
    }

    private static void AssertConstantsMirror(Type constantClass, Dictionary<string, string> expected)
    {
        var actual = constantClass
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, actual[pair.Key]);
        }
    }
}
