using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public partial class CodegenRegressionTests
{
    [Fact]
    public async Task ValueTaskOfT_AtRuntime_ReturnsAwaitedValue()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.VtRun
            {
                [RpcService]
                public interface IVtRun
                {
                    ValueTask<int> AddAsync(int a, int b);
                    ValueTask PingAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        finalCompilation.Emit(ms).Success.Should().BeTrue();
        ms.Position = 0;
        var alc = new System.Runtime.Loader.AssemblyLoadContext(
            "VtRun_" + Guid.NewGuid(), isCollectible: false);
        var asm = alc.LoadFromStream(ms);

        var client = new ValueReturningClient { NextResult = 12 };
        var proxyType = asm.GetType("Regress.VtRun.VtRunProxy")!;
        var proxy = Activator.CreateInstance(proxyType, client)!;

        // The proxy implements both IVtRun and the generated IVtRunAsync sibling, so
        // it carries two AddAsync overloads (with and without CancellationToken). Bind
        // explicitly to the original 2-arg overload so the lookup is unambiguous.
        var addMethod = proxyType.GetMethod("AddAsync", new[] { typeof(int), typeof(int) })!;
        var vt = addMethod.Invoke(proxy, new object[] { 4, 8 })!;
        var asTask = (Task<int>)vt.GetType().GetMethod("AsTask")!.Invoke(vt, null)!;
        (await asTask).Should().Be(12);

        // Same disambiguation for the zero-arg PingAsync (sibling adds a CT overload).
        var pingMethod = proxyType.GetMethod("PingAsync", Type.EmptyTypes)!;
        var vtPing = pingMethod.Invoke(proxy, Array.Empty<object>())!;
        var asTaskPing = (Task)vtPing.GetType().GetMethod("AsTask")!.Invoke(vtPing, null)!;
        await asTaskPing;
    }

    /// <summary>A client whose <c>Task&lt;TResponse&gt;</c> overload returns a configured value.</summary>
    private sealed class ValueReturningClient : global::DotBoxD.Services.Server.IRpcInvoker
    {
        public object? NextResult;
        public bool IsConnected => true;
        public Task ConnectAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;
        public Task<TR> InvokeAsync<TQ, TR>(string s, string m, TQ q, System.Threading.CancellationToken ct = default)
            => Task.FromResult((TR)NextResult!);
        public Task<TR> InvokeAsync<TR>(string s, string m, System.Threading.CancellationToken ct = default)
            => Task.FromResult((TR)NextResult!);
        public Task InvokeAsync<TQ>(string s, string m, TQ q, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;
        public Task InvokeAsync(string s, string m, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;

        // Feature-2 instance overloads forward to the singleton ones so the existing
        // assertions still observe sub-routed calls if a test were to exercise them.
        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(string s, string id, string m, TQ q, System.Threading.CancellationToken ct = default)
            => InvokeAsync<TQ, TR>(s, m, q, ct);
        public Task<TR> InvokeOnInstanceAsync<TR>(string s, string id, string m, System.Threading.CancellationToken ct = default)
            => InvokeAsync<TR>(s, m, ct);
        public Task InvokeOnInstanceAsync<TQ>(string s, string id, string m, TQ q, System.Threading.CancellationToken ct = default)
            => InvokeAsync<TQ>(s, m, q, ct);
        public Task InvokeOnInstanceAsync(string s, string id, string m, System.Threading.CancellationToken ct = default)
            => InvokeAsync(s, m, ct);
    }

    /// <summary>An IRpcInvoker that records which overload was actually called.</summary>
    private sealed class OverloadProbeClient : global::DotBoxD.Services.Server.IRpcInvoker
    {
        public int WithRequestWithResponseOverloadCalls;
        public int WithResponseOverloadCalls;
        public int WithRequestNoResponseOverloadCalls;
        public int NoRequestNoResponseOverloadCalls;

        public bool IsConnected => true;
        public Task ConnectAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;

        public Task<TR> InvokeAsync<TQ, TR>(string s, string m, TQ q, System.Threading.CancellationToken ct = default)
        {
            WithRequestWithResponseOverloadCalls++;
            return Task.FromResult(default(TR)!);
        }
        public Task<TR> InvokeAsync<TR>(string s, string m, System.Threading.CancellationToken ct = default)
        {
            WithResponseOverloadCalls++;
            return Task.FromResult(default(TR)!);
        }
        public Task InvokeAsync<TQ>(string s, string m, TQ q, System.Threading.CancellationToken ct = default)
        {
            WithRequestNoResponseOverloadCalls++;
            return Task.CompletedTask;
        }
        public Task InvokeAsync(string s, string m, System.Threading.CancellationToken ct = default)
        {
            NoRequestNoResponseOverloadCalls++;
            return Task.CompletedTask;
        }
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;

        // Feature-2 instance overloads forward to the singleton ones so the existing
        // assertions still observe sub-routed calls if a test were to exercise them.
        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(string s, string id, string m, TQ q, System.Threading.CancellationToken ct = default)
            => InvokeAsync<TQ, TR>(s, m, q, ct);
        public Task<TR> InvokeOnInstanceAsync<TR>(string s, string id, string m, System.Threading.CancellationToken ct = default)
            => InvokeAsync<TR>(s, m, ct);
        public Task InvokeOnInstanceAsync<TQ>(string s, string id, string m, TQ q, System.Threading.CancellationToken ct = default)
            => InvokeAsync<TQ>(s, m, q, ct);
        public Task InvokeOnInstanceAsync(string s, string id, string m, System.Threading.CancellationToken ct = default)
            => InvokeAsync(s, m, ct);
    }

}
