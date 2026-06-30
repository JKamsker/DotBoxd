using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class GeneratedReadonlyCollectionReaderTests
{
    [Fact]
    public void Direct_extension_list_reader_preserves_readonly_collection_shape()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ReadOnlyCollectionsSource);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List([KernelRpcValue.Int32(1)])));

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("ReadList")!
            .Invoke(null, [control]);

        AssertReadonlyList(result);
    }

    [Fact]
    public void Direct_extension_map_reader_preserves_readonly_collection_shape()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(ReadOnlyCollectionsSource);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Map(
            [
                KernelRpcValue.String("one"),
                KernelRpcValue.Int32(1)
            ])));

        var result = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("ReadMap")!
            .Invoke(null, [control]);

        AssertReadonlyMap(result);
    }

    private static object CreateControl(System.Reflection.Assembly assembly, byte[] response)
    {
        var controlType = assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!;
        return Activator.CreateInstance(controlType, [new RecordingRegistry(response)])!;
    }

    private static void AssertReadonlyList(object? value)
    {
        var list = Assert.IsAssignableFrom<IReadOnlyList<int>>(value);
        Assert.Equal([1], list);
        Assert.False(value is ICollection<int> { IsReadOnly: false });
    }

    private static void AssertReadonlyMap(object? value)
    {
        var map = Assert.IsAssignableFrom<IReadOnlyDictionary<string, int>>(value);
        Assert.Equal(1, map["one"]);
        Assert.False(value is ICollection<KeyValuePair<string, int>> { IsReadOnly: false });
    }

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public string PluginId<TService>()
            where TService : class
            => "readonly-collections";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(response);
        }
    }

    private const string ReadOnlyCollectionsSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;
        using DotBoxD.Abstractions;

        namespace Sample;

        [DotBoxDService]
        public interface IRemoteWorldControl
        {
        }

        public sealed class RemoteWorldControl : IRemoteWorldControl, IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                => ServerExtensions = serverExtensions;

            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        [ServerExtension(typeof(IRemoteWorldControl), "readonly-list")]
        public sealed partial class ReadOnlyListKernel
        {
            [ServerExtensionMethod(typeof(IRemoteWorldControl))]
            public IReadOnlyList<int> ReadList(HookContext ctx)
                => new List<int>();
        }

        [ServerExtension(typeof(IRemoteWorldControl), "readonly-map")]
        public sealed partial class ReadOnlyMapKernel
        {
            [ServerExtensionMethod(typeof(IRemoteWorldControl))]
            public IReadOnlyDictionary<string, int> ReadMap(HookContext ctx)
                => new Dictionary<string, int>();
        }

        public static class Probe
        {
            public static IReadOnlyList<int> ReadList(RemoteWorldControl control)
                => control.ReadList();

            public static IReadOnlyDictionary<string, int> ReadMap(RemoteWorldControl control)
                => control.ReadMap();
        }
        """;
}
