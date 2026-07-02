using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class TaskLikeSymbolRegressionTests
{
    [Fact]
    public void Referenced_value_task_definition_is_classified_as_task_like()
    {
        var compilation = CSharpCompilation.Create(
            "ReferencedValueTaskContract",
            [CSharpSyntaxTree.ParseText("""
                namespace Sample
                {
                    public sealed class Service
                    {
                        public System.Threading.Tasks.ValueTask<int> RunAsync() => default;
                    }
                }
                """)],
            [
                MetadataReference.CreateFromFile(PackageReference(
                    "microsoft.netframework.referenceassemblies.net461",
                    "build/.NETFramework/v4.6.1/mscorlib.dll")),
                MetadataReference.CreateFromFile(PackageReference(
                    "system.threading.tasks.extensions",
                    "lib/net461/System.Threading.Tasks.Extensions.dll"))
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetTypeByMetadataName("Sample.Service")!
            .GetMembers("RunAsync")
            .OfType<IMethodSymbol>()
            .Single();

        Assert.True(DotBoxDWellKnownTaskTypes.IsGenericValueTask(method.ReturnType, compilation, out var inner));
        Assert.Equal(SpecialType.System_Int32, inner.SpecialType);
    }

    [Fact]
    public void Registration_accumulator_rejects_source_defined_value_task_payload()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            namespace System.Threading.Tasks
            {
                public sealed class ValueTask<T>
                {
                }
            }

            namespace Sample
            {
                using DotBoxD.Abstractions;

                [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "Replace")]
                internal sealed class RemoteServiceControl
                {
                    public System.Threading.Tasks.ValueTask<string> Replace<TService, TKernel>()
                        where TService : class
                        where TKernel : class, TService
                        => new();
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("Task<T> or ValueTask<T>", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    [Fact]
    public void Server_extension_client_rejects_source_defined_task_payload_contract()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            namespace System.Threading.Tasks
            {
                public sealed class Task<T>
                {
                }
            }

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public interface ICounter
                {
                    System.Threading.Tasks.Task<int> RunAsync(int value);
                }

                [ServerExtension("fake-task-client", typeof(ICounter))]
                public sealed partial class CounterKernel
                {
                    public int Run(int value, HookContext ctx) => value;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("return type must match", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    private static string PackageReference(string packageId, string relativePath)
    {
        var packageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            packageId);
        var match = Directory.EnumerateFiles(packageRoot, Path.GetFileName(relativePath), SearchOption.AllDirectories)
            .Where(path => path.Replace(Path.DirectorySeparatorChar, '/').EndsWith(relativePath, StringComparison.Ordinal))
            .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        Assert.True(match is not null, $"Expected restored package asset '{packageId}/{relativePath}'.");
        return match;
    }
}
