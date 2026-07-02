using System.Reflection;
using System.Runtime.Loader;
using DotBoxD.Services.Generated;
using DotBoxD.Services.Server;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

internal static class GeneratedFactoryRegistryTestSupport
{
    public static Assembly CompileAndLoad(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var errors = runResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        using var stream = new MemoryStream();
        var emit = finalCompilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        stream.Position = 0;
        var context = new AssemblyLoadContext("FactoryTest_" + Guid.NewGuid().ToString("N"), isCollectible: false);
        return context.LoadFromStream(stream);
    }

    public static MetadataReference CompileGeneratedReference(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(driver.GetRunResult().GeneratedTrees);

        using var stream = new MemoryStream();
        var emit = finalCompilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}

internal interface INotGeneratedService
{
}

internal sealed class RegistrationSink : IRpcServiceRegistrationSink
{
    public List<ServiceRegistration> Services { get; } = new();

    public void AddService<TService, TImplementation>()
        where TService : class
        where TImplementation : TService =>
        Services.Add(new ServiceRegistration(typeof(TService), typeof(TImplementation)));
}

internal readonly record struct ServiceRegistration(Type ServiceType, Type ImplementationType);

internal sealed class GeneratedRegistrationSink : IRpcGeneratedServiceRegistrationSink
{
    public List<GeneratedServiceRegistration> Services { get; } = new();

    public void AddService<TService, TProxy, TDispatcher>()
        where TService : class
        where TProxy : TService
        where TDispatcher : IServiceDispatcher =>
        Services.Add(new GeneratedServiceRegistration(
            typeof(TService),
            typeof(TProxy),
            typeof(TDispatcher)));
}

internal readonly record struct GeneratedServiceRegistration(
    Type ServiceType,
    Type ProxyType,
    Type DispatcherType);

internal sealed class NullClient : global::DotBoxD.Services.Server.IRpcInvoker
{
    public bool IsConnected => true;

    public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => default;

    public Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<TResponse> InvokeAsync<TResponse>(
        string service,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task InvokeAsync<TRequest>(
        string service,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task InvokeAsync(string service, string method, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<TResponse> InvokeOnInstanceAsync<TRequest, TResponse>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<TResponse> InvokeOnInstanceAsync<TResponse>(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task InvokeOnInstanceAsync<TRequest>(
        string service,
        string instanceId,
        string method,
        TRequest request,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        CancellationToken ct = default) =>
        throw new NotSupportedException();
}
