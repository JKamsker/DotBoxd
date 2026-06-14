using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Client;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;

namespace ShaRPC.SourceGenerator.Tests;

public class CustomSubServiceWireNameRuntimeTests
{
    private const string Source = """
        using ShaRPC.Core.Attributes;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Reviewed.CustomSubWire
        {
            [ShaRpcService(Name = "sub-custom")]
            public interface ISub
            {
                Task<int> CountAsync(int value, CancellationToken ct = default);
            }

            [ShaRpcService(Name = "root-custom")]
            public interface IRoot
            {
                Task<ISub> OpenAsync(CancellationToken ct = default);
            }
        }
        """;

    [Fact]
    public async Task SubProxy_UsesCustomSubServiceNameForInstanceCalls()
    {
        var assembly = Compile(Source);
        var client = new RecordingClient
        {
            HandleResult = new ServiceHandle { ServiceName = "sub-custom", InstanceId = "sub-1" },
            CountResult = 42,
        };
        var rootProxy = Activator.CreateInstance(
            assembly.GetType("Reviewed.CustomSubWire.RootProxy")!,
            client)!;

        using var rootCts = new CancellationTokenSource();
        var open = rootProxy.GetType().GetMethod("OpenAsync", new[] { typeof(CancellationToken) })!;
        var sub = await AwaitObjectTask(open.Invoke(rootProxy, new object[] { rootCts.Token })!);

        using var subCts = new CancellationTokenSource();
        var count = sub.GetType().GetMethod("CountAsync", new[] { typeof(int), typeof(CancellationToken) })!;
        var result = await (Task<int>)count.Invoke(sub, new object[] { 7, subCts.Token })!;

        result.Should().Be(42);
        client.LastInstanceService.Should().Be("sub-custom");
        client.LastInstanceId.Should().Be("sub-1");
        client.LastInstanceMethod.Should().Be("CountAsync");
        client.LastInstanceRequest.Should().Be(7);
        client.LastInstanceCancellationToken.Should().Be(subCts.Token);
    }

    [Fact]
    public async Task RootDispatcher_RegistersAndRoutesCustomSubServiceName()
    {
        var assembly = Compile(Source);
        var subInterface = assembly.GetType("Reviewed.CustomSubWire.ISub")!;
        var rootInterface = assembly.GetType("Reviewed.CustomSubWire.IRoot")!;
        var sub = SubFactory.Create(subInterface);
        var root = RootFactory.Create(rootInterface, sub);
        var serializer = new TestJsonSerializer();
        var registry = new InstanceRegistry();

        var rootDispatcher = (IServiceDispatcher)Activator.CreateInstance(
            assembly.GetType("Reviewed.CustomSubWire.RootDispatcher")!,
            root)!;
        using var handleReply = await rootDispatcher.DispatchToPayloadAsync(
            "OpenAsync",
            System.ReadOnlyMemory<byte>.Empty,
            serializer,
            registry,
            CancellationToken.None);
        var handle = serializer.Deserialize<ServiceHandle>(handleReply.Memory);

        handle.ServiceName.Should().Be("sub-custom");
        registry.TryGet("sub-custom", handle.InstanceId, out var stored).Should().BeTrue();
        stored.Should().BeSameAs(sub);

        var subDispatcher = (IServiceDispatcher)Activator.CreateInstance(
            assembly.GetType("Reviewed.CustomSubWire.SubDispatcher")!,
            sub)!;
        using var countPayload = serializer.SerializeToPayload(9);
        using var countReply = await subDispatcher.DispatchOnInstanceToPayloadAsync(
            handle.InstanceId,
            "CountAsync",
            countPayload.Memory,
            serializer,
            registry,
            CancellationToken.None);

        serializer.Deserialize<int>(countReply.Memory).Should().Be(10);
    }

    private static Assembly Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var final = ((CSharpCompilation)compilation).AddSyntaxTrees(driver.GetRunResult().GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
        return Assembly.Load(ms.ToArray());
    }

    private static async Task<object> AwaitObjectTask(object task)
    {
        await (Task)task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private sealed class RecordingClient : global::ShaRPC.Core.IRpcInvoker
    {
        public ServiceHandle HandleResult { get; set; } = new();
        public int CountResult { get; set; }
        public string? LastInstanceService { get; private set; }
        public string? LastInstanceId { get; private set; }
        public string? LastInstanceMethod { get; private set; }
        public object? LastInstanceRequest { get; private set; }
        public CancellationToken LastInstanceCancellationToken { get; private set; }
        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;

        public Task<TR> InvokeAsync<TR>(string svc, string method, CancellationToken ct = default) =>
            Task.FromResult((TR)(object)HandleResult);

        public Task<TR> InvokeAsync<TQ, TR>(string svc, string method, TQ req, CancellationToken ct = default) =>
            Task.FromResult(default(TR)!);

        public Task InvokeAsync<TQ>(string svc, string method, TQ req, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvokeAsync(string svc, string method, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(
            string svc,
            string id,
            string method,
            TQ req,
            CancellationToken ct = default)
        {
            LastInstanceService = svc;
            LastInstanceId = id;
            LastInstanceMethod = method;
            LastInstanceRequest = req;
            LastInstanceCancellationToken = ct;
            return Task.FromResult((TR)(object)CountResult);
        }

        public Task<TR> InvokeOnInstanceAsync<TR>(string svc, string id, string method, CancellationToken ct = default) =>
            Task.FromResult(default(TR)!);

        public Task InvokeOnInstanceAsync<TQ>(
            string svc,
            string id,
            string method,
            TQ req,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task InvokeOnInstanceAsync(string svc, string id, string method, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private static class RootFactory
    {
        public static object Create(Type rootInterface, object sub)
        {
            var proxy = CreateProxy(rootInterface, typeof(RootStub));
            ((RootStub)proxy).Sub = sub;
            return proxy;
        }
    }

    private static class SubFactory
    {
        public static object Create(Type subInterface) => CreateProxy(subInterface, typeof(SubStub));
    }

    private static object CreateProxy(Type interfaceType, Type proxyType)
    {
        var open = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Create" && m.IsGenericMethodDefinition);
        return open.MakeGenericMethod(interfaceType, proxyType).Invoke(null, null)!;
    }

    public class RootStub : DispatchProxy
    {
        public object? Sub;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var resultType = targetMethod!.ReturnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new[] { Sub });
        }
    }

    public class SubStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Task.FromResult((int)args![0]! + 1);
    }

}
