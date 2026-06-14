using System;
using System.Collections.Generic;
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

/// <summary>
/// End-to-end coverage of Feature 2 (nested services): a root service method whose
/// return type is itself a <c>[ShaRpcService]</c> interface returns a generated
/// sub-service proxy bound to a server-side instance through a per-connection
/// <see cref="IInstanceRegistry"/>.
/// </summary>
public class NestedServiceTests
{
    private const string Source = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Nested.Demo
        {
            [ShaRpcService]
            public interface ISubService
            {
                Task<int> CountAsync();
                Task<int> SumAsync(int a, int b);
            }

            [ShaRpcService]
            public interface IRootService
            {
                Task<ISubService> GetSubAsync(string label);
            }
        }
        """;

    [Fact]
    public void RootMethod_ReturningSubService_ProducesServiceHandleOverTheWire()
    {
        var (asm, _) = Compile(Source);

        var proxyType = asm.GetType("Nested.Demo.RootServiceProxy")!;
        var iRoot = asm.GetType("Nested.Demo.IRootService")!;
        var iSub = asm.GetType("Nested.Demo.ISubService")!;
        proxyType.Should().NotBeNull();
        iRoot.IsAssignableFrom(proxyType).Should().BeTrue();

        // Verify the SUB proxy exists and accepts (IRpcInvoker, string).
        var subProxyType = asm.GetType("Nested.Demo.SubServiceProxy")!;
        subProxyType.Should().NotBeNull();
        subProxyType.GetConstructors().Should().Contain(c =>
            c.GetParameters().Length == 2
            && c.GetParameters()[0].ParameterType == typeof(global::ShaRPC.Core.IRpcInvoker)
            && c.GetParameters()[1].ParameterType == typeof(string));

        // Verify the proxy method's return type matches the interface (Task<ISubService>).
        // Bind explicitly to the original 1-arg overload since the async sibling adds a
        // second overload with CancellationToken.
        var getSub = proxyType.GetMethod("GetSubAsync", new[] { typeof(string) })!;
        getSub.ReturnType.Should().Be(typeof(Task<>).MakeGenericType(iSub));
    }

    [Fact]
    public async Task RootProxy_DeserializesServiceHandle_AndReturnsBoundSubProxy()
    {
        var (asm, _) = Compile(Source);

        var iSub = asm.GetType("Nested.Demo.ISubService")!;
        var proxyType = asm.GetType("Nested.Demo.RootServiceProxy")!;

        // The client returns a ServiceHandle for GetSubAsync (the wire response),
        // then a number for the sub-proxy's CountAsync.
        var fakeClient = new HandleClient
        {
            HandleResult = new ServiceHandle { ServiceName = "ISubService", InstanceId = "abc123" },
            CountResult = 42,
        };

        var topCtor = proxyType.GetConstructors().Single(c => c.GetParameters().Length == 1);
        var rootProxy = topCtor.Invoke(new object[] { fakeClient });
        // Bind to the original 1-arg overload — the async sibling adds a 2-arg overload.
        var getSubMethod = proxyType.GetMethod("GetSubAsync", new[] { typeof(string) })!;
        var getTask = (Task)getSubMethod.Invoke(rootProxy, new object[] { "x" })!;
        await getTask;
        var sub = getTask.GetType().GetProperty("Result")!.GetValue(getTask)!;
        iSub.IsInstanceOfType(sub).Should().BeTrue();

        // The next call on the sub-proxy must route via InvokeOnInstanceAsync with the
        // exact instance id the wire returned.
        var countMethod = iSub.GetMethod("CountAsync", Type.EmptyTypes)!;
        var countTask = (Task<int>)countMethod.Invoke(sub, Array.Empty<object>())!;
        (await countTask).Should().Be(42);

        fakeClient.LastInstanceCallService.Should().Be("ISubService");
        fakeClient.LastInstanceCallId.Should().Be("abc123");
        fakeClient.LastInstanceCallMethod.Should().Be("CountAsync");
    }

    [Fact]
    public async Task RootDispatcher_RegistersReturnedInstance_AndShipsServiceHandle()
    {
        var (asm, _) = Compile(Source);

        var dispatcherType = asm.GetType("Nested.Demo.RootServiceDispatcher")!;
        var iRoot = asm.GetType("Nested.Demo.IRootService")!;
        var iSub = asm.GetType("Nested.Demo.ISubService")!;

        // Build a real impl that returns a recorded sub instance.
        var subImpl = SubImplFactory.Create(iSub);
        var rootImpl = RootImplFactory.Create(iRoot, label => subImpl);

        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType, rootImpl)!;
        var registry = new InstanceRegistry();
        var serializer = new TestJsonSerializer();

        using var labelPayload = serializer.SerializeToPayload<string>("hello");
        using var reply = await dispatcher.DispatchToPayloadAsync("GetSubAsync", labelPayload.Memory, serializer, registry, CancellationToken.None);
        var handle = serializer.Deserialize<ServiceHandle>(reply.Memory);

        handle.ServiceName.Should().Be("ISubService");
        handle.InstanceId.Should().NotBeNullOrEmpty();

        // Registry must now hold the EXACT instance the user implementation returned.
        registry.TryGet("ISubService", handle.InstanceId, out var stored).Should().BeTrue();
        stored.Should().BeSameAs(subImpl);
    }

    [Fact]
    public async Task SubDispatcher_DispatchOnInstanceAsync_RoutesToRegisteredInstance()
    {
        var (asm, _) = Compile(Source);

        var dispatcherType = asm.GetType("Nested.Demo.SubServiceDispatcher")!;
        var iSub = asm.GetType("Nested.Demo.ISubService")!;

        // Register the sub-instance directly into a registry, then dispatch by id.
        var registry = new InstanceRegistry();
        var registeredSubImpl = SubImplFactory.Create(iSub, fixedCount: 7);
        var constructorSubImpl = SubImplFactory.Create(iSub, fixedCount: 99);
        var id = registry.Register("ISubService", registeredSubImpl);

        // Construct a dispatcher with a placeholder _service (any impl satisfies the
        // ctor — instance-scoped dispatch ignores it).
        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType, constructorSubImpl)!;
        var serializer = new TestJsonSerializer();

        using var reply = await dispatcher.DispatchOnInstanceToPayloadAsync(id, "CountAsync", System.ReadOnlyMemory<byte>.Empty, serializer, registry, CancellationToken.None);
        serializer.Deserialize<int>(reply.Memory).Should().Be(7,
            "instance-scoped dispatch must invoke the registry-resolved service, not the constructor service");

        // An unknown instance id must fail loudly with the framework's NotFound exception.
        await Assert.ThrowsAsync<ShaRPC.Core.Exceptions.ShaRpcNotFoundException>(async () =>
            await dispatcher.DispatchOnInstanceToPayloadAsync("does-not-exist", "CountAsync", System.ReadOnlyMemory<byte>.Empty, serializer, registry, CancellationToken.None));
    }

    [Fact]
    public async Task SubProxy_InstanceMethodWithPayload_UsesInvokeOnInstanceAsync()
    {
        var (asm, _) = Compile(Source);

        var proxyType = asm.GetType("Nested.Demo.RootServiceProxy")!;

        var fakeClient = new HandleClient
        {
            HandleResult = new ServiceHandle { ServiceName = "ISubService", InstanceId = "sum-123" },
            CountResult = 42,
        };

        var rootProxy = Activator.CreateInstance(proxyType, fakeClient)!;
        var getSubMethod = proxyType.GetMethod("GetSubAsync", new[] { typeof(string) })!;
        var getTask = (Task)getSubMethod.Invoke(rootProxy, new object[] { "payload" })!;
        await getTask;
        var sub = getTask.GetType().GetProperty("Result")!.GetValue(getTask)!;

        var sumMethod = sub.GetType().GetMethod("SumAsync", new[] { typeof(int), typeof(int) })!;
        var sumTask = (Task<int>)sumMethod.Invoke(sub, new object[] { 2, 3 })!;
        (await sumTask).Should().Be(42);

        fakeClient.LastInstanceCallService.Should().Be("ISubService");
        fakeClient.LastInstanceCallId.Should().Be("sum-123");
        fakeClient.LastInstanceCallMethod.Should().Be("SumAsync");
        fakeClient.LastInstanceRequest.Should().Be(new ValueTuple<int, int>(2, 3));

        using var cts = new CancellationTokenSource();
        var sumWithCt = sub.GetType().GetMethod(
            "SumAsync",
            new[] { typeof(int), typeof(int), typeof(CancellationToken) })!;
        var ctTask = (Task<int>)sumWithCt.Invoke(sub, new object[] { 5, 8, cts.Token })!;
        (await ctTask).Should().Be(42);
        fakeClient.LastInstanceRequest.Should().Be(new ValueTuple<int, int>(5, 8));
        fakeClient.LastInstanceCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public void ValueTaskSubServiceReturn_AcrossNamespaces_UsesQualifiedSubProxy()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Nested.Cross.Sub
            {
                [ShaRpcService]
                public interface ISubService
                {
                    ValueTask<int> CountAsync();
                }
            }

            namespace Nested.Cross.Root
            {
                [ShaRpcService]
                public interface IRootService
                {
                    ValueTask<Nested.Cross.Sub.ISubService> GetSubAsync();
                }
            }
            """;

        var (_, runResult) = Compile(source);

        var rootProxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRootService.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        rootProxy.Should().Contain("new global::Nested.Cross.Sub.SubServiceProxy");

        var rootDispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRootService.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        rootDispatcher.Should().Contain("registry.Register(\"ISubService\", __sub)");
    }

    [Fact]
    public void InstanceRegistry_ReleaseAll_RemovesEveryEntry()
    {
        var registry = new InstanceRegistry();
        var id1 = registry.Register("X", new object());
        var id2 = registry.Register("X", new object());
        registry.TryGet("X", id1, out _).Should().BeTrue();
        registry.TryGet("X", id2, out _).Should().BeTrue();

        registry.ReleaseAll();
        registry.TryGet("X", id1, out _).Should().BeFalse();
        registry.TryGet("X", id2, out _).Should().BeFalse();
    }

    // ---- helpers ----

    private static (Assembly Assembly, GeneratorDriverRunResult RunResult) Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var final = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException("Emit failed: " + errors);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("Nested_" + Guid.NewGuid(), isCollectible: false);
        return (alc.LoadFromStream(ms), runResult);
    }

    private sealed class HandleClient : global::ShaRPC.Core.IRpcInvoker
    {
        public ServiceHandle HandleResult { get; set; } = new();
        public int CountResult { get; set; }
        public string? LastInstanceCallService;
        public string? LastInstanceCallId;
        public string? LastInstanceCallMethod;
        public object? LastInstanceRequest;
        public CancellationToken LastInstanceCancellationToken;

        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;

        public Task<TR> InvokeAsync<TQ, TR>(string svc, string method, TQ req, CancellationToken ct = default)
        {
            // The root proxy's GetSubAsync path passes a request payload and expects
            // ServiceHandle back. Return the handle by cast.
            return Task.FromResult((TR)(object)HandleResult);
        }
        public Task<TR> InvokeAsync<TR>(string svc, string method, CancellationToken ct = default)
            => Task.FromResult(default(TR)!);
        public Task InvokeAsync<TQ>(string svc, string method, TQ req, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task InvokeAsync(string svc, string method, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(string svc, string id, string method, TQ req, CancellationToken ct = default)
        {
            LastInstanceCallService = svc; LastInstanceCallId = id; LastInstanceCallMethod = method;
            LastInstanceRequest = req;
            LastInstanceCancellationToken = ct;
            return Task.FromResult((TR)(object)CountResult);
        }
        public Task<TR> InvokeOnInstanceAsync<TR>(string svc, string id, string method, CancellationToken ct = default)
        {
            LastInstanceCallService = svc; LastInstanceCallId = id; LastInstanceCallMethod = method;
            LastInstanceCancellationToken = ct;
            return Task.FromResult((TR)(object)CountResult);
        }
        public Task InvokeOnInstanceAsync<TQ>(string svc, string id, string method, TQ req, CancellationToken ct = default)
        {
            LastInstanceCallService = svc; LastInstanceCallId = id; LastInstanceCallMethod = method;
            LastInstanceRequest = req;
            LastInstanceCancellationToken = ct;
            return Task.CompletedTask;
        }
        public Task InvokeOnInstanceAsync(string svc, string id, string method, CancellationToken ct = default)
        {
            LastInstanceCallService = svc; LastInstanceCallId = id; LastInstanceCallMethod = method;
            LastInstanceCancellationToken = ct;
            return Task.CompletedTask;
        }
    }

    /// <summary>Builds an in-memory <c>ISubService</c> implementation via DispatchProxy.</summary>
    private static class SubImplFactory
    {
        public static object Create(Type subIface, int fixedCount = 0)
        {
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(subIface, typeof(SubStub));
            var p = closed.Invoke(null, null)!;
            ((SubStub)p).Count = fixedCount;
            return p;
        }
    }

    public class SubStub : DispatchProxy
    {
        public int Count;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "CountAsync") return Task.FromResult(Count);
            if (targetMethod?.Name == "SumAsync") return Task.FromResult((int)args![0]! + (int)args[1]!);
            throw new InvalidOperationException("unexpected " + targetMethod?.Name);
        }
    }

    /// <summary>Builds an in-memory <c>IRootService</c> that hands out the supplied sub-impl.</summary>
    private static class RootImplFactory
    {
        public static object Create(Type rootIface, Func<string, object> mintSub)
        {
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(rootIface, typeof(RootStub));
            var p = closed.Invoke(null, null)!;
            ((RootStub)p).Mint = mintSub;
            return p;
        }
    }

    public class RootStub : DispatchProxy
    {
        public Func<string, object>? Mint;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetSubAsync")
            {
                var sub = Mint!((string)args![0]!);
                // Wrap into a Task<ISubService> via reflection.
                var iSub = targetMethod.ReturnType.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(iSub)
                    .Invoke(null, new[] { sub });
            }
            throw new InvalidOperationException("unexpected " + targetMethod?.Name);
        }
    }

}
