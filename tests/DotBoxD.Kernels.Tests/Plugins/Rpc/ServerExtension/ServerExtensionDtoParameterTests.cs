using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Proves a grafted <c>[ServerExtensionMethod]</c> entrypoint (the direct client path) accepts record/
/// value-object DTO parameters (issue #41): the generated extension marshals a DTO argument into a positional
/// <c>Record</c> wire value, supports a nested DTO and a plain <c>class</c> DTO, and a record return whose
/// field is a <c>List&lt;T&gt;</c> generates valid client code that round-trips. The proxy path and the
/// server-side reading of DTO parameters are covered in <see cref="ServerExtensionProxyTests"/> and
/// <see cref="RpcKernelGenerationTests"/>.
/// </summary>
public sealed class ServerExtensionDtoParameterTests
{
    private const string DirectRecordParamSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteWorldControl : IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public readonly record struct WorldRangeQuery(int Center, int Radius, int MaxResults);

        [ServerExtension(typeof(RemoteWorldControl), "range-query")]
        public sealed partial class RangeQueryKernel
        {
            [ServerExtensionMethod(typeof(RemoteWorldControl))]
            public int CountInRange(WorldRangeQuery query, HookContext ctx)
            {
                return query.Center + query.Radius + query.MaxResults;
            }
        }

        public static class Probe
        {
            public static int Count(RemoteWorldControl control, WorldRangeQuery query)
                => control.CountInRange(query);
        }
        """;

    private const string DirectNestedRecordParamSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteWorldControl : IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public readonly record struct WorldPoint(int Position);
        public readonly record struct WorldRangeQuery(WorldPoint Center, int Radius, int MaxResults);

        [ServerExtension(typeof(RemoteWorldControl), "range-query")]
        public sealed partial class RangeQueryKernel
        {
            [ServerExtensionMethod(typeof(RemoteWorldControl))]
            public int CountInRange(WorldRangeQuery query, HookContext ctx)
            {
                return query.Center.Position + query.Radius + query.MaxResults;
            }
        }

        public static class Probe
        {
            public static int Count(RemoteWorldControl control, WorldRangeQuery query)
                => control.CountInRange(query);
        }
        """;

    private const string DirectClassParamSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteWorldControl : IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public sealed class WorldRangeQuery
        {
            public WorldRangeQuery(int center, int radius, int maxResults)
            {
                Center = center;
                Radius = radius;
                MaxResults = maxResults;
            }

            public int Center { get; }
            public int Radius { get; }
            public int MaxResults { get; }
        }

        [ServerExtension(typeof(RemoteWorldControl), "range-query")]
        public sealed partial class RangeQueryKernel
        {
            [ServerExtensionMethod(typeof(RemoteWorldControl))]
            public int CountInRange(WorldRangeQuery query, HookContext ctx)
            {
                return query.Center + query.Radius + query.MaxResults;
            }
        }

        public static class Probe
        {
            public static int Count(RemoteWorldControl control, WorldRangeQuery query)
                => control.CountInRange(query);
        }
        """;

    private const string DirectRecordReturnWithListFieldSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed class RemoteWorldControl : IServerExtensionClientAccessor
        {
            public RemoteWorldControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions) => ServerExtensions = serverExtensions;
            public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
        }

        public readonly record struct Bag(int Id, List<int> Values);

        [ServerExtension(typeof(RemoteWorldControl), "bag")]
        public sealed partial class BagKernel
        {
            [ServerExtensionMethod(typeof(RemoteWorldControl))]
            public Bag Make(int id, HookContext ctx)
            {
                var values = new List<int>();
                values.Add(id);
                return new Bag(id, values);
            }
        }

        public static class Probe
        {
            public static Bag Make(RemoteWorldControl control, int id) => control.Make(id);
        }
        """;

    [Fact]
    public void Direct_extension_marshals_record_struct_parameter_to_a_record_value()
    {
        var arguments = InvokeDirectCount(DirectRecordParamSource, RecordArg(2, 3, 4), 9);

        var record = Assert.Single(arguments);
        record.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal([2, 3, 4], record.Items.Select(item => item.Int32Value));
    }

    [Fact]
    public void Direct_extension_marshals_nested_record_parameter_to_a_nested_record_value()
    {
        var arguments = InvokeDirectCount(DirectNestedRecordParamSource, WorldRangeQueryArg(5, 6, 7), 18);

        var record = Assert.Single(arguments);
        record.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(3, record.Items.Length);
        record.Items[0].RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(5, record.Items[0].Items[0].Int32Value);
        Assert.Equal(6, record.Items[1].Int32Value);
        Assert.Equal(7, record.Items[2].Int32Value);
    }

    [Fact]
    public void Direct_extension_marshals_class_dto_parameter_to_a_record_value()
    {
        var arguments = InvokeDirectCount(DirectClassParamSource, RecordArg(10, 20, 30), 60);

        var record = Assert.Single(arguments);
        record.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal([10, 20, 30], record.Items.Select(item => item.Int32Value));
    }

    [Fact]
    public void Direct_extension_record_return_with_list_field_round_trips()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DirectRecordReturnWithListFieldSource);
        var control = CreateControl(
            assembly,
            KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [
                KernelRpcValue.Int32(7),
                KernelRpcValue.List([KernelRpcValue.Int32(7), KernelRpcValue.Int32(8)])
            ])),
            out _);

        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;
        var bag = probe.GetMethod("Make", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, 7])!;

        var type = bag.GetType();
        Assert.Equal(7, type.GetProperty("Id")!.GetValue(bag));
        var values = Assert.IsAssignableFrom<IEnumerable<int>>(type.GetProperty("Values")!.GetValue(bag));
        Assert.Equal([7, 8], values);
    }

    private static KernelRpcValue[] InvokeDirectCount(string source, KernelRpcValue queryArg, int response)
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(source);
        var control = CreateControl(assembly, KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Int32(response)), out var registry);
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;
        var queryType = assembly.GetType("Sample.WorldRangeQuery", throwOnError: true)!;
        var query = MaterializeQuery(queryType, queryArg);

        var result = probe.GetMethod("Count", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [control, query]);

        Assert.Equal(response, result);
        return KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
    }

    private static object MaterializeQuery(Type queryType, KernelRpcValue value)
    {
        var constructor = queryType.GetConstructors().Single();
        var parameters = constructor.GetParameters();
        var ordered = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            ordered[i] = parameters[i].ParameterType == typeof(int)
                ? value.Items[i].Int32Value
                : MaterializeQuery(parameters[i].ParameterType, value.Items[i]);
        }

        return constructor.Invoke(ordered)!;
    }

    private static object CreateControl(Assembly assembly, byte[] response, out RecordingRegistry registry)
    {
        var controlType = assembly.GetType("Sample.RemoteWorldControl", throwOnError: true)!;
        registry = new RecordingRegistry(response);
        return Activator.CreateInstance(controlType, [registry])!;
    }

    private static KernelRpcValue RecordArg(int a, int b, int c)
        => KernelRpcValue.Record([KernelRpcValue.Int32(a), KernelRpcValue.Int32(b), KernelRpcValue.Int32(c)]);

    private static KernelRpcValue WorldRangeQueryArg(int center, int radius, int maxResults)
        => KernelRpcValue.Record(
        [
            KernelRpcValue.Record([KernelRpcValue.Int32(center)]),
            KernelRpcValue.Int32(radius),
            KernelRpcValue.Int32(maxResults)
        ]);

    private sealed class RecordingRegistry(byte[] response) : DotBoxD.Plugins.IServerExtensionClientRegistry
    {
        public byte[] LastArguments { get; private set; } = [];

        public string PluginId<TService>()
            where TService : class
            => "range-query";

        public ValueTask<byte[]> InvokeServerExtensionAsync(
            string pluginId,
            byte[] arguments,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastArguments = arguments;
            return ValueTask.FromResult(response);
        }
    }
}
