using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Behavior;

internal static class BehavioralTestSupport
{
    public static (Assembly Assembly, string GeneratedDump) CompileWithGenerator(string source)
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
            var errors = string.Join(
                "\n",
                emit.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            var dump = string.Join(
                "\n\n----\n\n",
                runResult.GeneratedTrees.Select(t => t.FilePath + "\n" + t.GetText()));
            throw new InvalidOperationException("Emit failed:\n" + errors + "\n\nGenerated:\n" + dump);
        }

        ms.Position = 0;
        var alc = new AssemblyLoadContext("BehavioralTest_" + Guid.NewGuid(), isCollectible: false);
        var asm = alc.LoadFromStream(ms);

        var generatedDump = string.Join(
            "\n\n",
            runResult.GeneratedTrees.Select(t => t.GetText().ToString()));
        return (asm, generatedDump);
    }
}

internal sealed class RecordingClient : global::DotBoxD.Services.Server.IRpcInvoker
{
    public string? LastService { get; private set; }
    public string? LastMethod { get; private set; }
    public object? LastRequest { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }
    public object? NextResultObject { get; set; }

    public bool IsConnected => true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
    {
        LastService = service;
        LastMethod = method;
        LastRequest = request;
        LastCancellationToken = ct;
        return Task.FromResult((TResponse)NextResultObject!);
    }

    public Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default)
    {
        LastService = service;
        LastMethod = method;
        LastCancellationToken = ct;
        return Task.FromResult((TResponse)NextResultObject!);
    }

    public Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default)
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

    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
        => InvokeAsync<TRequest, TResponse>(service, method, request, ct);

    public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
        => InvokeAsync<TResponse>(service, method, ct);

    public Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default)
        => InvokeAsync(service, method, request, ct);

    public Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default)
        => InvokeAsync(service, method, ct);
}

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

internal static class DispatchTargetFactory
{
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
        if (targetMethod is null || Impl is null)
        {
            return null;
        }

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
        if (targetMethod is null)
        {
            return null;
        }

        return targetMethod.Name switch
        {
            "AddAsync" => Impl!.AddAsync((int)args![0]!, (int)args[1]!, (CancellationToken)args[2]!),
            "SquareAsync" => Impl!.SquareAsync((int)args![0]!, (CancellationToken)args[1]!),
            "PingAsync" => Impl!.PingAsync((CancellationToken)args![0]!),
            _ => throw new InvalidOperationException("unsupported method: " + targetMethod.Name)
        };
    }
}
