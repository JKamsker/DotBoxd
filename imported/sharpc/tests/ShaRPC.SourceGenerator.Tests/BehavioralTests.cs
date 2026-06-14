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
using ShaRPC.Core.Attributes;
using ShaRPC.Core.Client;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Compiles and emits an assembly using the generated proxy/dispatcher and then
/// exercises them via reflection. Proves the generated code is wire-compatible
/// with the IRpcInvoker / IServiceDispatcher contracts.
/// </summary>
public class BehavioralTests
{
    private const string ServiceSource = """
        using ShaRPC.Core.Attributes;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Behavior.Demo
        {
            [ShaRpcService]
            public interface IMath
            {
                Task<int> AddAsync(int a, int b, CancellationToken ct = default);
                Task<int> SquareAsync(int x, CancellationToken ct = default);
                Task PingAsync(CancellationToken ct = default);
            }
        }
        """;

    [Fact]
    public async Task GeneratedProxy_ForwardsInvocationToInvoker_WithExpectedServiceAndMethodNames()
    {
        var (assembly, _) = CompileWithGenerator(ServiceSource);

        var proxyType = assembly.GetType("Behavior.Demo.MathProxy");
        proxyType.Should().NotBeNull("the generator should emit Behavior.Demo.MathProxy");

        var mockClient = new RecordingClient();
        mockClient.NextResultObject = 42;

        var proxy = Activator.CreateInstance(proxyType!, mockClient)!;

        var interfaceType = assembly.GetType("Behavior.Demo.IMath")!;
        var addMethod = interfaceType.GetMethod("AddAsync")!;

        // IMath.AddAsync declares (int, int, CancellationToken ct = default) — the proxy
        // now mirrors that signature exactly, so we pass the same three arguments.
        using var proxyCts = new CancellationTokenSource();
        var resultTask = (Task)addMethod.Invoke(proxy, new object[] { 2, 3, proxyCts.Token })!;
        await resultTask;
        var result = (int)resultTask.GetType().GetProperty("Result")!.GetValue(resultTask)!;

        result.Should().Be(42);
        mockClient.LastService.Should().Be("IMath");
        mockClient.LastMethod.Should().Be("AddAsync");
        mockClient.LastCancellationToken.Should().Be(proxyCts.Token);
    }

    [Fact]
    public async Task GeneratedDispatcher_DeserializesArgumentsAndInvokesServiceImplementation()
    {
        var (assembly, _) = CompileWithGenerator(ServiceSource);

        var dispatcherType = assembly.GetType("Behavior.Demo.MathDispatcher");
        dispatcherType.Should().NotBeNull("the generator should emit Behavior.Demo.MathDispatcher");

        var interfaceType = assembly.GetType("Behavior.Demo.IMath")!;
        var impl = new MathImpl();
        var implProxy = DispatchTargetFactory.CreateProxy(interfaceType, impl);

        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType!, implProxy)!;
        dispatcher.ServiceName.Should().Be("IMath");

        var serializer = new TestJsonSerializer();

        // Two-arg method goes through tuple deserialization.
        using var payload = serializer.SerializeToPayload<ValueTuple<int, int>>(new ValueTuple<int, int>(7, 5));
        using var dispatcherCts = new CancellationTokenSource();
        using var reply = await dispatcher.DispatchToPayloadAsync("AddAsync", payload.Memory, serializer, new InstanceRegistry(), dispatcherCts.Token);
        var sum = serializer.Deserialize<int>(reply.Memory);
        sum.Should().Be(12);
        impl.LastCall.Should().Be("Add(7,5)");
        impl.LastCancellationToken.Should().Be(dispatcherCts.Token);

        // One-arg method goes through single deserialization.
        using var single = serializer.SerializeToPayload<int>(6);
        using var reply2 = await dispatcher.DispatchToPayloadAsync("SquareAsync", single.Memory, serializer, new InstanceRegistry(), CancellationToken.None);
        var sq = serializer.Deserialize<int>(reply2.Memory);
        sq.Should().Be(36);

        // Zero-arg, void-returning method returns empty payload.
        using var pingReply = await dispatcher.DispatchToPayloadAsync("PingAsync", System.ReadOnlyMemory<byte>.Empty, serializer, new InstanceRegistry(), CancellationToken.None);
        pingReply.Length.Should().Be(0);
        impl.PingCount.Should().Be(1);
    }

    /// <summary>
    /// Regression for C-1: an interface method that does NOT declare a
    /// <see cref="CancellationToken"/> parameter must produce a proxy method whose
    /// signature exactly matches the interface (no spurious `ct` parameter), or the
    /// proxy class fails to implement the interface (CS0535).
    /// </summary>
    [Fact]
    public void InterfaceWithoutCancellationToken_GeneratesCompilingProxyAndDispatcher()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Behavior.NoCt
            {
                [ShaRpcService]
                public interface INoCt
                {
                    Task<int> AddAsync(int a, int b);
                    Task PingAsync();
                }
            }
            """;

        var (asm, _) = CompileWithGenerator(source);

        var interfaceType = asm.GetType("Behavior.NoCt.INoCt")!;
        var proxyType = asm.GetType("Behavior.NoCt.NoCtProxy")!;
        proxyType.Should().NotBeNull();
        interfaceType.IsAssignableFrom(proxyType).Should().BeTrue(
            "NoCtProxy must implement INoCt — the generator must not add stray parameters");

        var addParams = interfaceType.GetMethod("AddAsync")!.GetParameters();
        // The proxy implements both INoCt (the user interface, with no CT) and INoCtAsync
        // (the generated sibling, with CT). Resolve the original interface-matching
        // overload explicitly by parameter types to avoid an ambiguous-match exception.
        var proxyAddParams = proxyType
            .GetMethod("AddAsync", addParams.Select(p => p.ParameterType).ToArray())!
            .GetParameters();
        proxyAddParams.Should().HaveSameCount(addParams,
            "proxy must mirror the interface parameter list exactly");
    }

    /// <summary>
    /// Regression for C-2: a synchronous interface method (return type T, no Task) must
    /// produce a sync proxy method whose return type matches the interface, not
    /// `async Task&lt;T&gt;`.
    /// </summary>
    [Fact]
    public async Task InterfaceWithSyncReturn_GeneratesCompilingSyncProxyAndDispatcher()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Behavior.Sync
            {
                [ShaRpcService]
                public interface ISync
                {
                    int Add(int a, int b);
                    void Ping();
                }
            }
            """;

        var (asm, _) = CompileWithGenerator(source);

        var interfaceType = asm.GetType("Behavior.Sync.ISync")!;
        var proxyType = asm.GetType("Behavior.Sync.SyncProxy")!;
        proxyType.Should().NotBeNull();
        interfaceType.IsAssignableFrom(proxyType).Should().BeTrue(
            "SyncProxy must implement ISync with the declared sync return types");

        var addReturn = proxyType.GetMethod("Add")!.ReturnType;
        addReturn.Should().Be(typeof(int), "sync int return must stay int, not Task<int>");

        var pingReturn = proxyType.GetMethod("Ping")!.ReturnType;
        pingReturn.Should().Be(typeof(void), "sync void return must stay void, not Task");

        // Dispatcher should still work even for an all-sync service.
        var dispatcherType = asm.GetType("Behavior.Sync.SyncDispatcher")!;
        var syncImpl = new SyncImpl();
        var implProxy = DispatchTargetFactory.CreateProxyForInterface(interfaceType, syncImpl);
        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType, implProxy)!;
        var serializer = new TestJsonSerializer();

        using var addPayload = serializer.SerializeToPayload<ValueTuple<int, int>>(new ValueTuple<int, int>(4, 5));
        using var reply = await dispatcher.DispatchToPayloadAsync(
            "Add",
            addPayload.Memory,
            serializer,
            new InstanceRegistry(), CancellationToken.None);
        serializer.Deserialize<int>(reply.Memory).Should().Be(9);

        using var pingReply = await dispatcher.DispatchToPayloadAsync("Ping", System.ReadOnlyMemory<byte>.Empty, serializer, new InstanceRegistry(), CancellationToken.None);
        pingReply.Length.Should().Be(0);
        syncImpl.PingCalls.Should().Be(1);
    }

    [Fact]
    public void GeneratedExtensions_ExposeProvideAndGetPeerExtensionMethods()
    {
        var (assembly, _) = CompileWithGenerator(ServiceSource);

        var extType = assembly.GetType("ShaRPC.Generated.ShaRpcGeneratedExtensions");
        extType.Should().NotBeNull();

        var getMethod = extType!.GetMethod("GetMath", BindingFlags.Public | BindingFlags.Static);
        getMethod.Should().NotBeNull();
        getMethod!.GetParameters().Should().ContainSingle().Which.ParameterType.Should().Be(typeof(global::ShaRPC.Core.RpcPeer));

        var provideMethod = extType.GetMethod("ProvideMath", BindingFlags.Public | BindingFlags.Static);
        provideMethod.Should().NotBeNull();
        provideMethod!.GetParameters().Should().HaveCount(2);
        provideMethod.GetParameters()[0].ParameterType.Should().Be(typeof(global::ShaRPC.Core.RpcPeer));
    }

    // ---------- compilation infrastructure ----------

    private static (Assembly Assembly, string GeneratedDump) CompileWithGenerator(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            var dump = string.Join("\n\n----\n\n",
                runResult.GeneratedTrees.Select(t => t.FilePath + "\n" + t.GetText().ToString()));
            throw new InvalidOperationException("Emit failed:\n" + errors + "\n\nGenerated:\n" + dump);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("BehavioralTest_" + Guid.NewGuid(), isCollectible: false);
        var asm = alc.LoadFromStream(ms);

        var generatedDump = string.Join("\n\n",
            runResult.GeneratedTrees.Select(t => t.GetText().ToString()));
        return (asm, generatedDump);
    }

    // ---------- test doubles ----------

    private sealed class RecordingClient : global::ShaRPC.Core.IRpcInvoker
    {
        public string? LastService { get; private set; }
        public string? LastMethod { get; private set; }
        public object? LastRequest { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public object? NextResultObject { get; set; }

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            LastMethod = method;
            LastRequest = request;
            LastCancellationToken = ct;
            return Task.FromResult((TResponse)NextResultObject!);
        }

        public Task<TResponse> InvokeAsync<TResponse>(string service, string method, CancellationToken ct = default)
        {
            LastService = service;
            LastMethod = method;
            LastCancellationToken = ct;
            return Task.FromResult((TResponse)NextResultObject!);
        }

        public Task InvokeAsync<TRequest>(string service, string method, TRequest request, CancellationToken ct = default)
        {
            LastService = service;
            LastMethod = method;
            LastRequest = request;
            LastCancellationToken = ct;
            return Task.CompletedTask;
        }

        public Task InvokeAsync(string service, string method, CancellationToken ct = default)
        {
            LastService = service;
            LastMethod = method;
            LastCancellationToken = ct;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;

        // Forward Feature-2 instance overloads to the singleton recorders so the existing
        // assertions (LastService/LastMethod/LastRequest) still observe sub-routed calls.
        public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
            => InvokeAsync<TRequest, TResponse>(service, method, request, ct);
        public Task<TResponse> InvokeOnInstanceAsync<TResponse>(string service, string instanceId, string method, CancellationToken ct = default)
            => InvokeAsync<TResponse>(service, method, ct);
        public Task InvokeOnInstanceAsync<TRequest>(string service, string instanceId, string method, TRequest request, CancellationToken ct = default)
            => InvokeAsync(service, method, request, ct);
        public Task InvokeOnInstanceAsync(string service, string instanceId, string method, CancellationToken ct = default)
            => InvokeAsync(service, method, ct);
    }

    // Implementation of the in-memory IMath service. We can't statically reference the type
    // because it lives in the dynamically compiled assembly, so we use a dispatch proxy.
    public sealed class MathImpl
    {
        public string? LastCall { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public int PingCount { get; private set; }

        public Task<int> AddAsync(int a, int b, CancellationToken ct)
        {
            LastCall = $"Add({a},{b})";
            LastCancellationToken = ct;
            return Task.FromResult(a + b);
        }

        public Task<int> SquareAsync(int x, CancellationToken ct)
        {
            LastCall = $"Square({x})";
            LastCancellationToken = ct;
            return Task.FromResult(x * x);
        }

        public Task PingAsync(CancellationToken ct)
        {
            LastCancellationToken = ct;
            PingCount++;
            return Task.CompletedTask;
        }
    }

    private static class DispatchTargetFactory
    {
        // DispatchProxy.Create<TInterface, TProxy>() is a generic method.
        // We need to make it generic over (dynamicInterfaceType, MathDispatchProxy) at runtime.
        public static object CreateProxy(Type interfaceType, MathImpl impl)
        {
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(interfaceType, typeof(MathDispatchProxy));
            var proxy = closed.Invoke(null, null)!;
            ((MathDispatchProxy)proxy).Impl = impl;
            return proxy;
        }

        // Generic factory: forward every interface call to an arbitrary object via reflection
        // so tests can synthesize an implementation for a dynamically-compiled interface
        // without writing a per-interface DispatchProxy.
        public static object CreateProxyForInterface(Type interfaceType, object impl)
        {
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(interfaceType, typeof(ReflectiveDispatchProxy));
            var proxy = closed.Invoke(null, null)!;
            ((ReflectiveDispatchProxy)proxy).Impl = impl;
            return proxy;
        }
    }

    public class ReflectiveDispatchProxy : DispatchProxy
    {
        public object? Impl;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || Impl is null) return null;
            // Forward by name; assume the impl exposes a method with matching name + arity.
            var implMethod = Impl.GetType().GetMethod(
                targetMethod.Name,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: targetMethod.GetParameters().Select(p => p.ParameterType).ToArray(),
                modifiers: null);
            if (implMethod is null)
            {
                throw new InvalidOperationException(
                    $"Test impl {Impl.GetType().Name} has no method {targetMethod.Name} matching the requested signature.");
            }
            return implMethod.Invoke(Impl, args);
        }
    }

    public sealed class SyncImpl
    {
        public int PingCalls { get; private set; }
        public int Add(int a, int b) => a + b;
        public void Ping() => PingCalls++;
    }

    public class MathDispatchProxy : DispatchProxy
    {
        public MathImpl? Impl;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            switch (targetMethod.Name)
            {
                case "AddAsync":
                    return Impl!.AddAsync((int)args![0]!, (int)args[1]!, (CancellationToken)args[2]!);
                case "SquareAsync":
                    return Impl!.SquareAsync((int)args![0]!, (CancellationToken)args[1]!);
                case "PingAsync":
                    return Impl!.PingAsync((CancellationToken)args![0]!);
                default:
                    throw new InvalidOperationException("unsupported method: " + targetMethod.Name);
            }
        }
    }
}
