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

public class NullableSubServiceRuntimeTests
{
    private const string TaskSource = """
        #nullable enable
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Regress.NullableSubRuntime
        {
            [ShaRpcService]
            public interface ISub
            {
                Task<int> CountAsync();
            }

            [ShaRpcService]
            public interface IRoot
            {
                Task<ISub?> OpenAsync();
            }
        }
        """;

    private const string ValueTaskSource = """
        #nullable enable
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Regress.NullableSubRuntime
        {
            [ShaRpcService]
            public interface ISub
            {
                Task<int> CountAsync();
            }

            [ShaRpcService]
            public interface IRoot
            {
                ValueTask<ISub?> OpenAsync();
            }
        }
        """;

    [Theory]
    [InlineData(TaskSource)]
    [InlineData(ValueTaskSource)]
    public async Task Proxy_ReturnsNull_WhenNullableSubServiceHandleIsNull(string source)
    {
        var assembly = Compile(source);
        var proxy = Activator.CreateInstance(
            assembly.GetType("Regress.NullableSubRuntime.RootProxy")!,
            new NullHandleClient())!;

        var open = proxy.GetType().GetMethod("OpenAsync", Type.EmptyTypes)!;
        var sub = await AwaitObject(open.Invoke(proxy, Array.Empty<object>())!);

        sub.Should().BeNull();
    }

    [Theory]
    [InlineData(TaskSource)]
    [InlineData(ValueTaskSource)]
    public async Task Dispatcher_SerializesNullHandle_WhenNullableSubServiceIsNull(string source)
    {
        var assembly = Compile(source);
        var rootInterface = assembly.GetType("Regress.NullableSubRuntime.IRoot")!;
        var root = RootFactory.Create(rootInterface);
        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(
            assembly.GetType("Regress.NullableSubRuntime.RootDispatcher")!,
            root)!;
        var serializer = new TestJsonSerializer();

        using var reply = await dispatcher.DispatchToPayloadAsync(
            "OpenAsync",
            System.ReadOnlyMemory<byte>.Empty,
            serializer,
            new InstanceRegistry(),
            CancellationToken.None);

        serializer.Deserialize<ServiceHandle?>(reply.Memory).Should().BeNull();
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

    private static async Task<object?> AwaitObject(object taskLike)
    {
        if (taskLike is Task task)
        {
            await task;
        }
        else
        {
            var asTask = (Task)taskLike.GetType().GetMethod("AsTask")!
                .Invoke(taskLike, Array.Empty<object>())!;
            await asTask;
        }

        return taskLike.GetType().GetProperty("Result")!.GetValue(taskLike);
    }

    private sealed class NullHandleClient : global::ShaRPC.Core.IRpcInvoker
    {
        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
        public Task<TR> InvokeAsync<TR>(string svc, string method, CancellationToken ct = default) =>
            Task.FromResult(default(TR)!);
        public Task<TR> InvokeAsync<TQ, TR>(string svc, string method, TQ req, CancellationToken ct = default) =>
            Task.FromResult(default(TR)!);
        public Task InvokeAsync<TQ>(string svc, string method, TQ req, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task InvokeAsync(string svc, string method, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<TR> InvokeOnInstanceAsync<TR>(
            string svc,
            string id,
            string method,
            CancellationToken ct = default) =>
            Task.FromResult(default(TR)!);
        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(
            string svc,
            string id,
            string method,
            TQ req,
            CancellationToken ct = default) =>
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
        public static object Create(Type rootInterface)
        {
            var open = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition);
            return open.MakeGenericMethod(rootInterface, typeof(RootStub)).Invoke(null, null)!;
        }
    }

    public class RootStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var resultType = targetMethod!.ReturnType.GetGenericArguments()[0];
            var task = typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new object?[] { null });
            if (targetMethod.ReturnType.Name == "ValueTask`1")
            {
                return Activator.CreateInstance(targetMethod.ReturnType, task);
            }

            return task;
        }
    }

}
