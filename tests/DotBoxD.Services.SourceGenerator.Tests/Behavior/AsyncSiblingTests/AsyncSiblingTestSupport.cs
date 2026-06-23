using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Behavior;

internal static class AsyncSiblingTestSupport
{
    public static (Assembly Assembly, GeneratorDriverRunResult RunResult) Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var final = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        if (!emit.Success)
        {
            var errors = string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            var dump = string.Join(
                "\n----\n",
                final.SyntaxTrees.Select(t => t.FilePath + "\n" + t.GetText()));
            throw new InvalidOperationException("Emit failed: " + errors + "\n\n" + dump);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("AsyncSibling_" + Guid.NewGuid(), isCollectible: false);
        return (alc.LoadFromStream(ms), runResult);
    }
}

internal sealed class Recorder : global::DotBoxD.Services.Server.IRpcInvoker
{
    public object? NextResult;
    public string? LastService { get; private set; }
    public string? LastMethod { get; private set; }

    public bool IsConnected => true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<TR> InvokeAsync<TQ, TR>(
        string svc,
        string method,
        TQ req,
        CancellationToken ct = default)
    {
        LastService = svc;
        LastMethod = method;
        return Task.FromResult((TR)NextResult!);
    }

    public Task<TR> InvokeAsync<TR>(string svc, string method, CancellationToken ct = default)
    {
        LastService = svc;
        LastMethod = method;
        return Task.FromResult((TR)NextResult!);
    }

    public Task InvokeAsync<TQ>(string svc, string method, TQ req, CancellationToken ct = default)
    {
        LastService = svc;
        LastMethod = method;
        return Task.CompletedTask;
    }

    public Task InvokeAsync(string svc, string method, CancellationToken ct = default)
    {
        LastService = svc;
        LastMethod = method;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => default;

    public Task<TR> InvokeOnInstanceAsync<TQ, TR>(
        string svc,
        string id,
        string method,
        TQ req,
        CancellationToken ct = default)
        => InvokeAsync<TQ, TR>(svc, method, req, ct);

    public Task<TR> InvokeOnInstanceAsync<TR>(
        string svc,
        string id,
        string method,
        CancellationToken ct = default)
        => InvokeAsync<TR>(svc, method, ct);

    public Task InvokeOnInstanceAsync<TQ>(
        string svc,
        string id,
        string method,
        TQ req,
        CancellationToken ct = default)
        => InvokeAsync(svc, method, req, ct);

    public Task InvokeOnInstanceAsync(
        string svc,
        string id,
        string method,
        CancellationToken ct = default)
        => InvokeAsync(svc, method, ct);
}

internal sealed class DeferredRecorder(Task<object?> gate) : global::DotBoxD.Services.Server.IRpcInvoker
{
    public bool IsConnected => true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async Task<TR> InvokeAsync<TQ, TR>(
        string svc,
        string method,
        TQ req,
        CancellationToken ct = default)
    {
        var value = await gate.ConfigureAwait(false);
        return (TR)value!;
    }

    public async Task<TR> InvokeAsync<TR>(
        string svc,
        string method,
        CancellationToken ct = default)
    {
        var value = await gate.ConfigureAwait(false);
        return (TR)value!;
    }

    public Task InvokeAsync<TQ>(string svc, string method, TQ req, CancellationToken ct = default) =>
        gate;

    public Task InvokeAsync(string svc, string method, CancellationToken ct = default) =>
        gate;

    public ValueTask DisposeAsync() => default;

    public Task<TR> InvokeOnInstanceAsync<TQ, TR>(
        string svc,
        string id,
        string method,
        TQ req,
        CancellationToken ct = default)
        => InvokeAsync<TQ, TR>(svc, method, req, ct);

    public Task<TR> InvokeOnInstanceAsync<TR>(
        string svc,
        string id,
        string method,
        CancellationToken ct = default)
        => InvokeAsync<TR>(svc, method, ct);

    public Task InvokeOnInstanceAsync<TQ>(
        string svc,
        string id,
        string method,
        TQ req,
        CancellationToken ct = default)
        => InvokeAsync(svc, method, req, ct);

    public Task InvokeOnInstanceAsync(
        string svc,
        string id,
        string method,
        CancellationToken ct = default)
        => InvokeAsync(svc, method, ct);
}
