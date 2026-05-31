using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Client;
using ShaRPC.Core.Generated;
using ShaRPC.Core.Server;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests;

public class GeneratedFactoryRegistryTests
{
    [Fact]
    public void GeneratedFactory_CreatesProxyAndDispatcherWithoutScanning()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Factory.Sample
            {
                [ShaRpcService]
                public interface IGreeter
                {
                    Task<string> HelloAsync();
                }

                public sealed class Greeter : IGreeter
                {
                    public Task<string> HelloAsync() => Task.FromResult("hello");
                }
            }
            """;

        var assembly = CompileAndLoad(source);
        var interfaceType = assembly.GetType("Factory.Sample.IGreeter")!;
        var implementation = Activator.CreateInstance(assembly.GetType("Factory.Sample.Greeter")!)!;
        var client = new NullClient();

        var generated = assembly.GetType("ShaRPC.Generated.ShaRpcGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var proxy = generated
            .GetMethod("CreateProxy", new[] { typeof(Type), typeof(IShaRpcClient) })!
            .Invoke(null, new object[] { interfaceType, client });
        var dispatcher = generated
            .GetMethod("CreateDispatcher", new[] { typeof(Type), typeof(object) })!
            .Invoke(null, new[] { interfaceType, implementation });

        Assert.True(interfaceType.IsInstanceOfType(proxy));
        Assert.IsAssignableFrom<IServiceDispatcher>(dispatcher);

        var registryProxy = ShaRpcServiceRegistry.CreateProxy(interfaceType, client);
        var registryDispatcher = ShaRpcServiceRegistry.CreateDispatcher(interfaceType, implementation);

        Assert.True(interfaceType.IsInstanceOfType(registryProxy));
        Assert.Equal("IGreeter", registryDispatcher.ServiceName);
    }

    [Fact]
    public void Registry_ReportsClearDiagnosticWhenGeneratorDidNotRun()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ShaRpcServiceRegistry.CreateProxy(typeof(INotGeneratedService), new NullClient()));

        Assert.Contains("No ShaRPC generated factory is registered", ex.Message);
        Assert.Contains("[ShaRpcService]", ex.Message);
        Assert.Contains("source generator", ex.Message);
    }

    private interface INotGeneratedService
    {
    }

    private static Assembly CompileAndLoad(string source)
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

    private sealed class NullClient : IShaRpcClient
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
}
