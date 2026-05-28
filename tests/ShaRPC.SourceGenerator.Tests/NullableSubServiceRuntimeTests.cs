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
    private const string Source = """
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

    [Fact]
    public async Task Proxy_ReturnsNull_WhenNullableSubServiceHandleIsNull()
    {
        var assembly = Compile();
        var proxy = Activator.CreateInstance(
            assembly.GetType("Regress.NullableSubRuntime.RootProxy")!,
            new NullHandleClient())!;

        var open = proxy.GetType().GetMethod("OpenAsync", Type.EmptyTypes)!;
        var sub = await AwaitObjectTask(open.Invoke(proxy, Array.Empty<object>())!);

        sub.Should().BeNull();
    }

    [Fact]
    public async Task Dispatcher_SerializesNullHandle_WhenNullableSubServiceIsNull()
    {
        var assembly = Compile();
        var rootInterface = assembly.GetType("Regress.NullableSubRuntime.IRoot")!;
        var root = RootFactory.Create(rootInterface);
        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(
            assembly.GetType("Regress.NullableSubRuntime.RootDispatcher")!,
            root)!;
        var serializer = new JsonSerializerWrapper();

        var bytes = await dispatcher.DispatchAsync(
            "OpenAsync",
            Array.Empty<byte>(),
            serializer,
            new InstanceRegistry(),
            CancellationToken.None);

        serializer.Deserialize<ServiceHandle?>(bytes).Should().BeNull();
    }

    private static Assembly Compile()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source);
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

    private static async Task<object?> AwaitObjectTask(object task)
    {
        await (Task)task;
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private sealed class NullHandleClient : IShaRpcClient
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
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(resultType)
                .Invoke(null, new object?[] { null });
        }
    }

    private sealed class JsonSerializerWrapper : ISerializer
    {
        private static readonly System.Text.Json.JsonSerializerOptions s_options = new() { IncludeFields = true };
        public byte[] Serialize<T>(T value) =>
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, s_options);
        public T Deserialize<T>(ReadOnlySpan<byte> data) =>
            System.Text.Json.JsonSerializer.Deserialize<T>(data, s_options)!;
        public object? Deserialize(ReadOnlySpan<byte> data, Type type) =>
            System.Text.Json.JsonSerializer.Deserialize(data, type, s_options);
    }
}
