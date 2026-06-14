using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Round 6 regression for the generated sub-service dispatch cleanup (DispatcherGenerator). When
/// <c>registry.Register(...)</c> throws (e.g. the per-connection registry is at capacity) AND the orphaned
/// sub-service's <c>DisposeAsync()</c> ALSO throws during cleanup, the disposal exception escaped the catch
/// block before the <c>throw;</c> rethrow, replacing the original registration failure. The remote caller
/// (and diagnostics) then saw the wrong root cause. Cleanup must be best-effort so the original exception
/// is preserved. (Mirrors the R1#2 leak harness, but the sub-service's DisposeAsync now faults.)
/// </summary>
public sealed class Round6_DispatcherSubServiceDisposeThrowTests
{
    private const string Source = """
        using ShaRPC.Core.Attributes;
        using System;
        using System.Threading.Tasks;

        namespace Round6.SubDispose
        {
            [ShaRpcService]
            public interface ISubService : IAsyncDisposable
            {
                Task<int> CountAsync();
            }

            [ShaRpcService]
            public interface IRootService
            {
                Task<ISubService> GetSubAsync(string label);
            }
        }
        """;

    [Fact]
    public async Task Dispatcher_PreservesRegistrationError_WhenSubServiceDisposeAlsoThrows()
    {
        var asm = Compile(Source);

        var dispatcherType = asm.GetType("Round6.SubDispose.RootServiceDispatcher")!;
        var iRoot = asm.GetType("Round6.SubDispose.IRootService")!;
        var iSub = asm.GetType("Round6.SubDispose.ISubService")!;

        var subImpl = SubImplFactory.Create(iSub);
        var rootImpl = RootImplFactory.Create(iRoot, _ => subImpl);
        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType, rootImpl)!;

        // Registry at capacity so Register throws InvalidOperationException — the REAL failure.
        var registry = new InstanceRegistry(maxInstances: 1);
        registry.Register("Filler", new object());

        var serializer = new TestJsonSerializer();
        using var labelPayload = serializer.SerializeToPayload<string>("hello");

        // The registration failure must reach the caller, NOT the NotSupportedException raised by the
        // sub-service's DisposeAsync during cleanup. RED today: the disposal exception escapes instead.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.DispatchToPayloadAsync(
                "GetSubAsync",
                labelPayload.Memory,
                serializer,
                registry,
                CancellationToken.None));
    }

    private static Assembly Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var final = ((CSharpCompilation)compilation).AddSyntaxTrees(driver.GetRunResult().GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        Assert.True(
            emit.Success,
            string.Join(
                "\n",
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        return Assembly.Load(ms.ToArray());
    }

    private static class SubImplFactory
    {
        public static object Create(Type subIface)
        {
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(subIface, typeof(ThrowingDisposeSubStub));
            return closed.Invoke(null, null)!;
        }
    }

    public class ThrowingDisposeSubStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "DisposeAsync")
            {
                // A faulting async disposer: the cleanup path must not let this mask the registry failure.
                return new ValueTask(Task.FromException(new NotSupportedException("dispose boom")));
            }

            if (targetMethod?.Name == "CountAsync")
            {
                return Task.FromResult(0);
            }

            throw new InvalidOperationException("unexpected " + targetMethod?.Name);
        }
    }

    private static class RootImplFactory
    {
        public static object Create(Type rootIface, Func<string, object> mintSub)
        {
            var openGeneric = typeof(DispatchProxy)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2);
            var closed = openGeneric.MakeGenericMethod(rootIface, typeof(RootStub));
            var proxy = closed.Invoke(null, null)!;
            ((RootStub)proxy).Mint = mintSub;
            return proxy;
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
                var iSub = targetMethod.ReturnType.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(iSub)
                    .Invoke(null, new[] { sub });
            }

            throw new InvalidOperationException("unexpected " + targetMethod?.Name);
        }
    }
}
