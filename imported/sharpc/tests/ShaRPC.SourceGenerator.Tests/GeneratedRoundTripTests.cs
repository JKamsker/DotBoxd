using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Client;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// End-to-end round-trip tests that wire a generated client proxy <em>directly</em> into the
/// matching generated server dispatcher through a real serializer. Unlike
/// <see cref="BehavioralTests"/> — which drives the proxy and the dispatcher separately with
/// hand-built payloads — these prove the two halves of generated code agree on the wire
/// format: whatever the proxy serializes, the dispatcher can deserialize, and the result the
/// dispatcher serializes is exactly what the proxy expects back. Coverage spans argument
/// counts (0/1/2/3/4), return kinds (Task, Task&lt;T&gt;, sync, void), reference-type and
/// collection payloads, enums, nullable strings, custom wire names, the unknown-method error
/// path, and multiple services compiled together.
/// </summary>
public class GeneratedRoundTripTests
{
    [Fact]
    public async Task ArgumentCountMatrix_ProxyAndDispatcher_AgreeOnTheWire()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Matrix
            {
                [ShaRpcService]
                public interface IMatrix
                {
                    Task<int> NoArgsAsync(CancellationToken ct = default);
                    Task<int> OneArgAsync(int x, CancellationToken ct = default);
                    Task<int> TwoArgsAsync(int a, int b, CancellationToken ct = default);
                    Task<int> ThreeArgsAsync(int a, int b, int c, CancellationToken ct = default);
                    Task<long> FourArgsAsync(int a, int b, int c, int d, CancellationToken ct = default);
                    Task SetAsync(int x, CancellationToken ct = default);
                    Task PingAsync(CancellationToken ct = default);
                }

                public sealed class MatrixServer : IMatrix
                {
                    public int LastSet { get; private set; } = -1;
                    public int PingCount { get; private set; }

                    public Task<int> NoArgsAsync(CancellationToken ct = default) => Task.FromResult(7);
                    public Task<int> OneArgAsync(int x, CancellationToken ct = default) => Task.FromResult(x * x);
                    public Task<int> TwoArgsAsync(int a, int b, CancellationToken ct = default) => Task.FromResult(a + b);
                    public Task<int> ThreeArgsAsync(int a, int b, int c, CancellationToken ct = default) => Task.FromResult(a + b + c);
                    public Task<long> FourArgsAsync(int a, int b, int c, int d, CancellationToken ct = default) => Task.FromResult((long)a + b + c + d);
                    public Task SetAsync(int x, CancellationToken ct = default) { LastSet = x; return Task.CompletedTask; }
                    public Task PingAsync(CancellationToken ct = default) { PingCount++; return Task.CompletedTask; }
                }
            }
            """;

        var h = Harness.Build(source, "RoundTrip.Matrix.IMatrix", "RoundTrip.Matrix.MatrixServer");

        (await h.CallAsync("NoArgsAsync")).Should().Be(7,
            "the zero-argument-with-return path must select Task<TResponse> InvokeAsync(service, method, ct) and the dispatcher must answer it with no payload to deserialize");
        (await h.CallAsync("OneArgAsync", 6)).Should().Be(36,
            "the single-argument path serializes the bare value, not a 1-tuple");
        (await h.CallAsync("TwoArgsAsync", 7, 5)).Should().Be(12,
            "two arguments travel as a ValueTuple and the dispatcher must read args.Item1/Item2");
        (await h.CallAsync("ThreeArgsAsync", 1, 2, 3)).Should().Be(6,
            "three arguments must round-trip via a 3-element ValueTuple (args.Item3)");
        (await h.CallAsync("FourArgsAsync", 1, 2, 3, 4)).Should().Be(10L,
            "four arguments must round-trip via a 4-element ValueTuple (args.Item4)");

        // No-return paths: the call must actually reach the implementation, so verify the
        // observable side effect rather than a return value.
        (await h.CallAsync("SetAsync", 99)).Should().BeNull("SetAsync returns a non-generic Task");
        ((int)h.GetImplProperty("LastSet")!).Should().Be(99,
            "the one-argument-no-return path must deliver the argument to the service");

        await h.CallAsync("PingAsync");
        ((int)h.GetImplProperty("PingCount")!).Should().Be(1,
            "the zero-argument-void path must still invoke the service exactly once");
    }

    [Fact]
    public async Task ReferenceTypesCollectionsAndEnums_RoundTripThroughGeneratedCode()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Data
            {
                public enum Color { Red = 1, Green = 2, Blue = 3 }

                public record Point(int X, int Y);

                [ShaRpcService]
                public interface IData
                {
                    Task<Point> EchoPointAsync(Point p, CancellationToken ct = default);
                    Task<Point> CombineAsync(Point a, Point b, CancellationToken ct = default);
                    Task<List<string>> ReverseAsync(List<string> items, CancellationToken ct = default);
                    Task<int[]> DoubleAllAsync(int[] values, CancellationToken ct = default);
                    Task<Color> NextAsync(Color c, CancellationToken ct = default);
                    Task<string?> MaybeUpperAsync(string? input, CancellationToken ct = default);
                }

                public sealed class DataServer : IData
                {
                    public Task<Point> EchoPointAsync(Point p, CancellationToken ct = default) => Task.FromResult(p);
                    public Task<Point> CombineAsync(Point a, Point b, CancellationToken ct = default) => Task.FromResult(new Point(a.X + b.X, a.Y + b.Y));
                    public Task<List<string>> ReverseAsync(List<string> items, CancellationToken ct = default) { items.Reverse(); return Task.FromResult(items); }
                    public Task<int[]> DoubleAllAsync(int[] values, CancellationToken ct = default) => Task.FromResult(values.Select(v => v * 2).ToArray());
                    public Task<Color> NextAsync(Color c, CancellationToken ct = default) => Task.FromResult((Color)(((int)c % 3) + 1));
                    public Task<string?> MaybeUpperAsync(string? input, CancellationToken ct = default) => Task.FromResult(input?.ToUpperInvariant());
                }
            }
            """;

        var h = Harness.Build(source, "RoundTrip.Data.IData", "RoundTrip.Data.DataServer");
        var pointType = h.LoadType("RoundTrip.Data.Point");
        var colorType = h.LoadType("RoundTrip.Data.Color");

        // Single reference-type argument and reference-type return.
        var point = Activator.CreateInstance(pointType, 3, 4)!;
        var echoed = (await h.CallAsync("EchoPointAsync", point))!;
        ReadInt(echoed, "X").Should().Be(3);
        ReadInt(echoed, "Y").Should().Be(4);

        // Two reference-type arguments => a tuple of DTOs must round-trip.
        var a = Activator.CreateInstance(pointType, 1, 2)!;
        var b = Activator.CreateInstance(pointType, 10, 20)!;
        var combined = (await h.CallAsync("CombineAsync", a, b))!;
        ReadInt(combined, "X").Should().Be(11);
        ReadInt(combined, "Y").Should().Be(22);

        // Generic collection argument and return.
        var reversed = (List<string>)(await h.CallAsync("ReverseAsync", new List<string> { "a", "b", "c" }))!;
        reversed.Should().Equal("c", "b", "a");

        // Array argument and return.
        var doubled = (int[])(await h.CallAsync("DoubleAllAsync", new[] { 2, 3, 4 }))!;
        doubled.Should().Equal(4, 6, 8);

        // Enum argument and return.
        var next = (await h.CallAsync("NextAsync", Enum.ToObject(colorType, 2)))!; // Green -> Blue
        Convert.ToInt32(next).Should().Be(3);

        // Nullable string: both the present and the null case must survive the wire.
        (await h.CallAsync("MaybeUpperAsync", "hello")).Should().Be("HELLO");
        (await h.CallAsync("MaybeUpperAsync", new object?[] { null })).Should().BeNull(
            "a null reference-type argument must serialize and deserialize back to null");
    }

    [Fact]
    public async Task CustomServiceAndMethodWireNames_RouteCorrectlyAtRuntime()
    {
        // The dispatcher's switch is keyed on the wire name; the proxy emits that same wire
        // name. String-match tests already assert the generated literals — this proves the
        // two literals actually meet at runtime end-to-end.
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Custom
            {
                [ShaRpcService(Name = "calc-svc")]
                public interface ICalculator
                {
                    [ShaRpcMethod(Name = "do-add")]
                    Task<int> AddAsync(int a, int b, CancellationToken ct = default);
                }

                public sealed class CalculatorServer : ICalculator
                {
                    public Task<int> AddAsync(int a, int b, CancellationToken ct = default) => Task.FromResult(a + b);
                }
            }
            """;

        var h = Harness.Build(source, "RoundTrip.Custom.ICalculator", "RoundTrip.Custom.CalculatorServer");

        h.Dispatcher.ServiceName.Should().Be("calc-svc",
            "the dispatcher must advertise the custom [ShaRpcService(Name=...)] wire name");
        (await h.CallAsync("AddAsync", 20, 22)).Should().Be(42,
            "the proxy must call service 'calc-svc' / method 'do-add' and the dispatcher must resolve that exact pair");
    }

    [Fact]
    public async Task DispatchAsync_WithUnknownMethod_ThrowsShaRpcNotFoundException()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Errors
            {
                [ShaRpcService]
                public interface IThing
                {
                    Task<int> GetAsync(CancellationToken ct = default);
                }

                public sealed class ThingServer : IThing
                {
                    public Task<int> GetAsync(CancellationToken ct = default) => Task.FromResult(1);
                }
            }
            """;

        var h = Harness.Build(source, "RoundTrip.Errors.IThing", "RoundTrip.Errors.ThingServer");

        var ex = await Assert.ThrowsAsync<ShaRPC.Core.Exceptions.ShaRpcNotFoundException>(async () =>
            await h.Dispatcher.DispatchToPayloadAsync(
                "NoSuchMethod", System.ReadOnlyMemory<byte>.Empty, h.Serializer, new InstanceRegistry(), CancellationToken.None));

        ex.Message.Should().Contain("NoSuchMethod",
            "the default switch branch must name the missing method");
        ex.Message.Should().Contain("IThing",
            "the default switch branch must name the service for diagnosability");
    }

    [Fact]
    public async Task MultipleServicesInOneCompilation_EachRoundTripsIndependently()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace RoundTrip.Multi
            {
                [ShaRpcService]
                public interface IAlpha
                {
                    Task<int> IncAsync(int x, CancellationToken ct = default);
                }

                [ShaRpcService]
                public interface IBeta
                {
                    Task<string> GreetAsync(string who, CancellationToken ct = default);
                }

                public sealed class AlphaServer : IAlpha
                {
                    public Task<int> IncAsync(int x, CancellationToken ct = default) => Task.FromResult(x + 1);
                }

                public sealed class BetaServer : IBeta
                {
                    public Task<string> GreetAsync(string who, CancellationToken ct = default) => Task.FromResult("hi " + who);
                }
            }
            """;

        var asm = CompileAndLoad(source);
        var serializer = new TestJsonSerializer();
        var registry = new InstanceRegistry();
        var client = new LoopbackClient(serializer, registry);

        var alpha = Harness.Attach(asm, client, "RoundTrip.Multi.IAlpha", "RoundTrip.Multi.AlphaServer", serializer, registry);
        var beta = Harness.Attach(asm, client, "RoundTrip.Multi.IBeta", "RoundTrip.Multi.BetaServer", serializer, registry);

        (await alpha.CallAsync("IncAsync", 41)).Should().Be(42,
            "the first service must route through its own dispatcher");
        (await beta.CallAsync("GreetAsync", "bob")).Should().Be("hi bob",
            "the second service must route through its own dispatcher without interference");
    }

    [Fact]
    public async Task SyncAndVoidMethods_RoundTripThroughGeneratedBlockingProxyPaths()
    {
        // Exercises the proxy's blocking emit paths: Sync (`return ....GetResult()`) and
        // Void (`....GetResult()`), wired to a real dispatcher over the loopback.
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace RoundTrip.Sync
            {
                [ShaRpcService]
                public interface ICounter
                {
                    int Add(int a, int b);
                    void Reset();
                    int Get();
                }

                public sealed class CounterServer : ICounter
                {
                    private int _value;
                    public int Add(int a, int b) { _value = a + b; return _value; }
                    public void Reset() => _value = 0;
                    public int Get() => _value;
                }
            }
            """;

        var h = Harness.Build(source, "RoundTrip.Sync.ICounter", "RoundTrip.Sync.CounterServer");

        ((int)(await h.CallAsync("Add", 4, 5))!).Should().Be(9,
            "a synchronous value-returning method must block on the loopback and return the value");
        ((int)(await h.CallAsync("Get"))!).Should().Be(9,
            "server state set by the previous sync call must be observable through a zero-arg sync getter");
        (await h.CallAsync("Reset")).Should().BeNull("Reset is void");
        ((int)(await h.CallAsync("Get"))!).Should().Be(0,
            "the void Reset call must have reached the server and mutated its state");
    }

    // ---------------------------------------------------------------------------------------
    // Harness: compile the user source with the generator, then connect the generated proxy
    // and dispatcher through an in-memory loopback client.
    // ---------------------------------------------------------------------------------------

    private sealed class Harness
    {
        private readonly object _proxy;
        private readonly object _impl;
        private readonly Type _interfaceType;

        public Assembly Assembly { get; }
        public IServiceDispatcher Dispatcher { get; }
        public ISerializer Serializer { get; }

        private Harness(Assembly assembly, object proxy, object impl, IServiceDispatcher dispatcher, ISerializer serializer, Type interfaceType)
        {
            Assembly = assembly;
            _proxy = proxy;
            _impl = impl;
            Dispatcher = dispatcher;
            Serializer = serializer;
            _interfaceType = interfaceType;
        }

        public static Harness Build(string source, string interfaceFqn, string implFqn)
        {
            var asm = CompileAndLoad(source);
            var serializer = new TestJsonSerializer();
            var registry = new InstanceRegistry();
            var client = new LoopbackClient(serializer, registry);
            return Attach(asm, client, interfaceFqn, implFqn, serializer, registry);
        }

        /// <summary>
        /// Wires one service (interface + impl) inside an already-loaded assembly into the
        /// supplied loopback client. Multiple services can share one client and registry.
        /// </summary>
        public static Harness Attach(
            Assembly asm,
            LoopbackClient client,
            string interfaceFqn,
            string implFqn,
            ISerializer serializer,
            InstanceRegistry registry)
        {
            var interfaceType = asm.GetType(interfaceFqn)
                ?? throw new InvalidOperationException($"interface {interfaceFqn} not found in generated assembly");
            var implType = asm.GetType(implFqn)
                ?? throw new InvalidOperationException($"impl {implFqn} not found in generated assembly");

            var impl = Activator.CreateInstance(implType)!;
            var dispatcherType = FindGenerated(asm, "Dispatcher", t =>
                t.GetConstructors().Any(c =>
                {
                    var p = c.GetParameters();
                    return p.Length == 1 && p[0].ParameterType == interfaceType;
                }));
            var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType, impl)!;
            client.Register(dispatcher);

            var proxyType = FindGenerated(asm, "Proxy", interfaceType.IsAssignableFrom);
            var proxy = Activator.CreateInstance(proxyType, client)!;

            return new Harness(asm, proxy, impl, dispatcher, serializer, interfaceType);
        }

        /// <summary>Invokes a proxy method (resolved by the interface's exact parameter
        /// types so the async-sibling overloads never cause an ambiguous match) and awaits
        /// whatever it returns — Task, Task&lt;T&gt;, ValueTask, ValueTask&lt;T&gt;, a sync
        /// value, or void.</summary>
        public async Task<object?> CallAsync(string method, params object?[] args)
        {
            var interfaceMethod = _interfaceType.GetMethod(method)
                ?? throw new InvalidOperationException($"interface has no method {method}");
            var parameterTypes = interfaceMethod.GetParameters().Select(p => p.ParameterType).ToArray();

            // Convenience: callers omit the trailing CancellationToken; supply the default.
            if (parameterTypes.Length == args.Length + 1 && parameterTypes[^1] == typeof(CancellationToken))
            {
                args = args.Append((object)CancellationToken.None).ToArray();
            }

            var proxyMethod = _proxy.GetType().GetMethod(method, parameterTypes)
                ?? throw new InvalidOperationException($"proxy has no method {method} with the interface signature");

            var result = proxyMethod.Invoke(_proxy, args);
            return await AwaitDynamic(result, proxyMethod.ReturnType);
        }

        public Type LoadType(string fqn) =>
            Assembly.GetType(fqn) ?? throw new InvalidOperationException($"type {fqn} not found");

        public object? GetImplProperty(string name) =>
            _impl.GetType().GetProperty(name)!.GetValue(_impl);

        private static Type FindGenerated(Assembly asm, string suffix, Func<Type, bool> predicate) =>
            asm.GetTypes().Single(t =>
                t.IsClass && !t.IsAbstract && t.Name.EndsWith(suffix, StringComparison.Ordinal) && predicate(t));
    }

    private static async Task<object?> AwaitDynamic(object? result, Type returnType)
    {
        switch (result)
        {
            case null:
                return null;
            case Task task:
                await task.ConfigureAwait(false);
                return returnType.IsGenericType ? returnType.GetProperty("Result")!.GetValue(task) : null;
            default:
                var runtimeType = result.GetType();
                if (runtimeType == typeof(ValueTask))
                {
                    await ((ValueTask)result).ConfigureAwait(false);
                    return null;
                }
                if (runtimeType.IsGenericType && runtimeType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                {
                    var asTask = (Task)runtimeType.GetMethod("AsTask")!.Invoke(result, null)!;
                    await asTask.ConfigureAwait(false);
                    return asTask.GetType().GetProperty("Result")!.GetValue(asTask);
                }
                return result; // synchronous value
        }
    }

    private static int ReadInt(object instance, string property) =>
        (int)instance.GetType().GetProperty(property)!.GetValue(instance)!;

    private static Assembly CompileAndLoad(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generator must not report errors for a supported service");

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            var dump = string.Join("\n\n----\n\n",
                runResult.GeneratedTrees.Select(t => t.FilePath + "\n" + t.GetText()));
            throw new InvalidOperationException("Emit failed:\n" + errors + "\n\nGenerated:\n" + dump);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("RoundTripTest_" + Guid.NewGuid(), isCollectible: false);
        return alc.LoadFromStream(ms);
    }

    // ---------------------------------------------------------------------------------------
    // Test doubles
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// An <see cref="ShaRPC.Core.IRpcInvoker"/> whose every Invoke overload serializes the request,
    /// hands the bytes straight to the matching generated dispatcher, and deserializes the
    /// reply — the wire transport collapsed to a method call. Routing is by service name so a
    /// single client can front several services in one compilation.
    /// </summary>
    internal sealed class LoopbackClient : global::ShaRPC.Core.IRpcInvoker
    {
        private readonly ISerializer _serializer;
        private readonly IInstanceRegistry _registry;
        private readonly Dictionary<string, IServiceDispatcher> _dispatchers = new(StringComparer.Ordinal);

        public LoopbackClient(ISerializer serializer, IInstanceRegistry registry)
        {
            _serializer = serializer;
            _registry = registry;
        }

        public void Register(IServiceDispatcher dispatcher) =>
            _dispatchers[dispatcher.ServiceName] = dispatcher;

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;

        private IServiceDispatcher Resolve(string service) =>
            _dispatchers.TryGetValue(service, out var dispatcher)
                ? dispatcher
                : throw new InvalidOperationException($"no dispatcher registered for service '{service}'");

        public async Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string method, TRequest request, CancellationToken ct = default)
        {
            using var p = _serializer.SerializeToPayload(request);
            using var reply = await Resolve(service).DispatchToPayloadAsync(method, p.Memory, _serializer, _registry, ct);
            return _serializer.Deserialize<TResponse>(reply.Memory);
        }

        public async Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default)
        {
            using var reply = await Resolve(service).DispatchToPayloadAsync(method, System.ReadOnlyMemory<byte>.Empty, _serializer, _registry, ct);
            return _serializer.Deserialize<TResponse>(reply.Memory);
        }

        public async Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default)
        {
            using var p = _serializer.SerializeToPayload(request);
            using var reply = await Resolve(service).DispatchToPayloadAsync(method, p.Memory, _serializer, _registry, ct);
        }

        public async Task InvokeAsync(string service, string method, CancellationToken ct = default)
        {
            using var reply = await Resolve(service).DispatchToPayloadAsync(method, System.ReadOnlyMemory<byte>.Empty, _serializer, _registry, ct);
        }

        public async Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
        {
            using var p = _serializer.SerializeToPayload(request);
            using var reply = await Resolve(service).DispatchOnInstanceToPayloadAsync(instanceId, method, p.Memory, _serializer, _registry, ct);
            return _serializer.Deserialize<TResponse>(reply.Memory);
        }

        public async Task<TResponse> InvokeOnInstanceAsync<TResponse>(string service, string instanceId, string method, CancellationToken ct = default)
        {
            using var reply = await Resolve(service).DispatchOnInstanceToPayloadAsync(instanceId, method, System.ReadOnlyMemory<byte>.Empty, _serializer, _registry, ct);
            return _serializer.Deserialize<TResponse>(reply.Memory);
        }

        public async Task InvokeOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
        {
            using var p = _serializer.SerializeToPayload(request);
            using var reply = await Resolve(service).DispatchOnInstanceToPayloadAsync(instanceId, method, p.Memory, _serializer, _registry, ct);
        }

        public async Task InvokeOnInstanceAsync(string service, string instanceId, string method, CancellationToken ct = default)
        {
            using var reply = await Resolve(service).DispatchOnInstanceToPayloadAsync(instanceId, method, System.ReadOnlyMemory<byte>.Empty, _serializer, _registry, ct);
        }
    }
}
