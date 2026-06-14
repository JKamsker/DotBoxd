using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
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

public class ReviewedNestedValueTaskRuntimeTests
{
    private const string Source = """
        using ShaRPC.Core.Attributes;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Reviewed.NestedValueTask
        {
            [ShaRpcService]
            public interface ISub
            {
                ValueTask<int> CountAsync(int value, CancellationToken ct = default);
            }

            [ShaRpcService]
            public interface IRoot
            {
                ValueTask<ISub> OpenAsync(string label, CancellationToken ct = default);
            }
        }
        """;

    [Fact]
    public async Task ValueTaskSubServiceProxy_PassesCancellationToken_AndOmitsItFromPayload()
    {
        var (assembly, _) = Compile(Source);
        var client = new RecordingClient
        {
            HandleResult = new ServiceHandle { ServiceName = "ISub", InstanceId = "sub-1" },
            CountResult = 42,
        };
        var rootProxy = Activator.CreateInstance(
            assembly.GetType("Reviewed.NestedValueTask.RootProxy")!,
            client)!;

        using var rootCts = new CancellationTokenSource();
        var sub = await AsTask(
            rootProxy.GetType().GetMethod("OpenAsync", new[] { typeof(string), typeof(CancellationToken) })!
                .Invoke(rootProxy, new object[] { "alpha", rootCts.Token })!);

        client.LastRequest.Should().Be("alpha");
        client.LastCancellationToken.Should().Be(rootCts.Token);

        using var subCts = new CancellationTokenSource();
        var result = await AsTask<int>(
            sub.GetType().GetMethod("CountAsync", new[] { typeof(int), typeof(CancellationToken) })!
                .Invoke(sub, new object[] { 7, subCts.Token })!);

        result.Should().Be(42);
        client.LastInstanceRequest.Should().Be(7);
        client.LastInstanceCancellationToken.Should().Be(subCts.Token);
    }

    [Fact]
    public async Task ValueTaskSubServiceDispatch_RegistersInstance_AndRoutesWithCancellationToken()
    {
        var (assembly, _) = Compile(Source);
        var subInterface = assembly.GetType("Reviewed.NestedValueTask.ISub")!;
        var rootInterface = assembly.GetType("Reviewed.NestedValueTask.IRoot")!;
        var sub = SubFactory.Create(subInterface);
        var root = RootFactory.Create(rootInterface, sub);
        var serializer = new TestJsonSerializer();
        var registry = new InstanceRegistry();

        using var rootCts = new CancellationTokenSource();
        var rootDispatcher = (IServiceDispatcher)Activator.CreateInstance(
            assembly.GetType("Reviewed.NestedValueTask.RootDispatcher")!,
            root)!;
        using var openPayload = serializer.SerializeToPayload("alpha");
        using var handleReply = await rootDispatcher.DispatchToPayloadAsync(
            "OpenAsync",
            openPayload.Memory,
            serializer,
            registry,
            rootCts.Token);
        var handle = serializer.Deserialize<ServiceHandle>(handleReply.Memory);

        registry.TryGet("ISub", handle.InstanceId, out var stored).Should().BeTrue();
        stored.Should().BeSameAs(sub);
        ((RootStub)root).LastCancellationToken.Should().Be(rootCts.Token);

        using var subCts = new CancellationTokenSource();
        var subDispatcher = (IServiceDispatcher)Activator.CreateInstance(
            assembly.GetType("Reviewed.NestedValueTask.SubDispatcher")!,
            sub)!;
        using var countPayload = serializer.SerializeToPayload(9);
        using var countReply = await subDispatcher.DispatchOnInstanceToPayloadAsync(
            handle.InstanceId,
            "CountAsync",
            countPayload.Memory,
            serializer,
            registry,
            subCts.Token);

        serializer.Deserialize<int>(countReply.Memory).Should().Be(10);
        ((SubStub)sub).LastValue.Should().Be(9);
        ((SubStub)sub).LastCancellationToken.Should().Be(subCts.Token);
    }

    private static async Task<object> AsTask(object valueTask)
    {
        var task = (Task)valueTask.GetType().GetMethod("AsTask")!.Invoke(valueTask, null)!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static async Task<T> AsTask<T>(object valueTask)
    {
        var task = (Task<T>)valueTask.GetType().GetMethod("AsTask")!.Invoke(valueTask, null)!;
        return await task;
    }

    private static (Assembly Assembly, GeneratorDriverRunResult RunResult) Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var final = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        ms.Position = 0;
        var alc = new AssemblyLoadContext("ReviewedNestedValueTask_" + Guid.NewGuid(), isCollectible: false);
        return (alc.LoadFromStream(ms), runResult);
    }

    private sealed class RecordingClient : global::ShaRPC.Core.IRpcInvoker
    {
        public ServiceHandle HandleResult { get; set; } = new();
        public int CountResult { get; set; }
        public object? LastRequest { get; private set; }
        public object? LastInstanceRequest { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public CancellationToken LastInstanceCancellationToken { get; private set; }
        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;

        public Task<TR> InvokeAsync<TQ, TR>(string svc, string method, TQ req, CancellationToken ct = default)
        {
            LastRequest = req;
            LastCancellationToken = ct;
            return Task.FromResult((TR)(object)HandleResult);
        }

        public Task<TR> InvokeAsync<TR>(string svc, string method, CancellationToken ct = default) =>
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
        public CancellationToken LastCancellationToken;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            LastCancellationToken = (CancellationToken)args![1]!;
            var valueTaskType = targetMethod!.ReturnType;
            return Activator.CreateInstance(valueTaskType, Sub);
        }
    }

    public class SubStub : DispatchProxy
    {
        public int LastValue;
        public CancellationToken LastCancellationToken;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            LastValue = (int)args![0]!;
            LastCancellationToken = (CancellationToken)args[1]!;
            return new ValueTask<int>(LastValue + 1);
        }
    }

}
