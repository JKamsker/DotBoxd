using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDecimalSupportTests
{
    private const string DecimalEchoSource = """
        using System;
        using System.Threading.Tasks;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public readonly record struct MoneyQuote(decimal Amount, decimal? OptionalAmount);

        public interface IAccountingService
        {
            ValueTask<MoneyQuote> EchoAsync(MoneyQuote value, decimal? adjustment);
        }

        [ServerExtension("accounting", typeof(IAccountingService))]
        public sealed partial class AccountingKernel
        {
            public MoneyQuote Echo(MoneyQuote value, decimal? adjustment, HookContext ctx)
                => new(value.Amount, adjustment);
        }

        public static class Probe
        {
            public static ValueTask<MoneyQuote> Echo(
                AccountingKernelServerExtensionClient client,
                MoneyQuote value,
                decimal? adjustment)
                => client.EchoAsync(value, adjustment);
        }
        """;

    [Fact]
    public void Marshaller_round_trips_decimal_as_four_int_bits()
    {
        const decimal value = -1234567890123456789.012300m;

        var sandbox = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(value, typeof(decimal)));

        Assert.Equal(DecimalSandboxType(), sandbox.Type);
        AssertDecimalSandbox(sandbox, value);
        AssertDecimalBits(value, Assert.IsType<decimal>(
            KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(decimal))));
        AssertDecimalBits(value, Assert.IsType<decimal>(
            KernelRpcMarshaller.FromKernelRpcValue(DecimalWire(value), typeof(decimal))));
        Assert.Equal(DecimalSandboxType(), KernelRpcMarshaller.SandboxTypeOf(typeof(decimal)));
    }

    [Fact]
    public void Marshaller_round_trips_nullable_decimal_as_has_value_bits_record()
    {
        const decimal value = 9876543210.000120m;

        var present = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(value, typeof(decimal?)));
        var absent = Assert.IsType<RecordValue>(
            KernelRpcMarshaller.ToSandboxValue(null, typeof(decimal?)));

        AssertNullableDecimalSandbox(present, value);
        AssertNullableDecimalSandbox(absent, null);
        AssertDecimalBits(value, Assert.IsType<decimal>(
            KernelRpcMarshaller.FromSandboxValue(present, typeof(decimal?))));
        Assert.Null(KernelRpcMarshaller.FromSandboxValue(absent, typeof(decimal?)));
        AssertDecimalBits(value, Assert.IsType<decimal>(
            KernelRpcMarshaller.FromKernelRpcValue(NullableDecimalWire(value), typeof(decimal?))));
        Assert.Null(KernelRpcMarshaller.FromKernelRpcValue(NullableDecimalWire(null), typeof(decimal?)));
        Assert.Equal(
            SandboxType.Record([SandboxType.Bool, DecimalSandboxType()]),
            KernelRpcMarshaller.SandboxTypeOf(typeof(decimal?)));
    }

    [Fact]
    public async Task Generated_client_round_trips_decimal_dto_and_nullable_decimal()
    {
        const decimal inputAmount = 1234.500m;
        decimal? inputOptional = null;
        decimal? adjustment = -98765.432100m;
        const decimal responseAmount = -42.4200m;
        decimal? responseOptional = 0.000001m;
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(DecimalEchoSource);
        var payloadType = assembly.GetType("Sample.MoneyQuote", throwOnError: true)!;
        var input = Activator.CreateInstance(payloadType, [inputAmount, inputOptional])!;
        var registry = new RecordingServerExtensionWireClient(MoneyQuoteWireBytes(responseAmount, responseOptional));
        var client = CreateClient(assembly, registry);

        var result = await InvokeEchoAsync(assembly, client, input, adjustment);

        AssertDecimalBits(responseAmount, DecimalProperty(payloadType, result, "Amount"));
        AssertDecimalBits(responseOptional, NullableDecimalProperty(payloadType, result, "OptionalAmount"));
        var arguments = KernelRpcBinaryCodec.DecodeArguments(registry.LastArguments);
        Assert.Equal(2, arguments.Length);
        AssertMoneyQuoteWire(arguments[0], inputAmount, inputOptional);
        AssertNullableDecimalWire(arguments[1], adjustment);
    }

    [Fact]
    public async Task Runtime_proxy_allows_decimal_and_nullable_decimal_service_contracts()
    {
        const decimal amount = 13579.246800m;
        decimal? adjustment = -111.222300m;
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            DecimalEchoSource,
            "Sample.AccountingPluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<IDecimalAccountingService>(kernel);

        var result = await service.EchoAsync(new DecimalQuote(amount, null), adjustment);

        AssertDecimalBits(amount, result.Amount);
        AssertDecimalBits(adjustment, result.OptionalAmount);
    }

    private static object CreateClient(Assembly assembly, RecordingServerExtensionWireClient registry)
    {
        var clientType = assembly.GetType("Sample.AccountingKernelServerExtensionClient", throwOnError: true)!;
        return clientType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry, "accounting"])!;
    }

    private static async Task<object> InvokeEchoAsync(
        Assembly assembly,
        object client,
        object value,
        decimal? adjustment)
    {
        var valueTask = assembly.GetType("Sample.Probe", throwOnError: true)!
            .GetMethod("Echo", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [client, value, adjustment])!;
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        var task = (Task)asTask.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static byte[] MoneyQuoteWireBytes(decimal amount, decimal? optional)
        => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(
            [DecimalWire(amount), NullableDecimalWire(optional)]));

    private static KernelRpcValue DecimalWire(decimal value)
    {
        var bits = decimal.GetBits(value);
        return KernelRpcValue.Record(
        [
            KernelRpcValue.Int32(bits[0]),
            KernelRpcValue.Int32(bits[1]),
            KernelRpcValue.Int32(bits[2]),
            KernelRpcValue.Int32(bits[3])
        ]);
    }

    private static KernelRpcValue NullableDecimalWire(decimal? value)
        => KernelRpcValue.Record(
        [
            KernelRpcValue.Bool(value.HasValue),
            DecimalWire(value.GetValueOrDefault())
        ]);

    private static void AssertMoneyQuoteWire(KernelRpcValue value, decimal amount, decimal? optional)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        AssertDecimalWire(value.GetItem(0), amount);
        AssertNullableDecimalWire(value.GetItem(1), optional);
    }

    private static void AssertNullableDecimalWire(KernelRpcValue value, decimal? expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(2, value.ItemCount);
        Assert.Equal(expected.HasValue, value.GetItem(0).BoolValue);
        AssertDecimalWire(value.GetItem(1), expected.GetValueOrDefault());
    }

    private static void AssertDecimalWire(KernelRpcValue value, decimal expected)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        Assert.Equal(4, value.ItemCount);
        Assert.Equal(decimal.GetBits(expected), Enumerable.Range(0, 4).Select(i => value.GetItem(i).Int32Value).ToArray());
    }

    private static void AssertNullableDecimalSandbox(RecordValue record, decimal? expected)
    {
        Assert.Equal(2, record.Fields.Count);
        Assert.Equal(expected.HasValue, Assert.IsType<BoolValue>(record.Fields[0]).Value);
        AssertDecimalSandbox(Assert.IsType<RecordValue>(record.Fields[1]), expected.GetValueOrDefault());
    }

    private static void AssertDecimalSandbox(RecordValue record, decimal expected)
    {
        Assert.Equal(4, record.Fields.Count);
        Assert.Equal(decimal.GetBits(expected), record.Fields.Cast<I32Value>().Select(field => field.Value).ToArray());
    }

    private static decimal DecimalProperty(Type type, object instance, string name)
        => Assert.IsType<decimal>(type.GetProperty(name)!.GetValue(instance));

    private static decimal? NullableDecimalProperty(Type type, object instance, string name)
        => type.GetProperty(name)!.GetValue(instance) is { } value ? Assert.IsType<decimal>(value) : null;

    private static void AssertDecimalBits(decimal expected, decimal actual)
        => Assert.Equal(decimal.GetBits(expected), decimal.GetBits(actual));

    private static void AssertDecimalBits(decimal? expected, decimal? actual)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue)
        {
            AssertDecimalBits(expected.Value, actual!.Value);
        }
    }

    private static SandboxType DecimalSandboxType()
        => SandboxType.Record([SandboxType.I32, SandboxType.I32, SandboxType.I32, SandboxType.I32]);

    private interface IDecimalAccountingService
    {
        ValueTask<DecimalQuote> EchoAsync(DecimalQuote value, decimal? adjustment);
    }

    private readonly record struct DecimalQuote(decimal Amount, decimal? OptionalAmount);

    private sealed class RecordingServerExtensionWireClient(byte[] response) : DotBoxD.Plugins.IServerExtensionWireClient
    {
        public byte[] LastArguments { get; private set; } = [];

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
