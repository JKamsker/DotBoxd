using DotBoxD.Services.Generated;
using Microsoft.CodeAnalysis;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class GeneratedFactoryRegistryDefaultValueTests
{
    [Fact]
    public void GeneratedMetadata_BoxedDefaultsPreserveDefaultLiteralValueTypes()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using DotBoxD.Services.Attributes;

            namespace Metadata.DefaultLiteral
            {
                public enum Status
                {
                    None,
                    Ready
                }

                [DotBoxDService]
                public interface IDefaults
                {
                    Task<int> EchoAsync(
                        int count = default,
                        Status status = default,
                        Guid id = default,
                        DateTime at = default);
                }
            }
            """;

        var assembly = CompileAndLoad(source);
        var serviceType = assembly.GetType("Metadata.DefaultLiteral.IDefaults")!;
        var statusType = assembly.GetType("Metadata.DefaultLiteral.Status")!;
        var generated = assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<GeneratedService>>(
            generated.GetProperty("Services")!.GetValue(null));

        var service = services.Single(candidate => candidate.ServiceType == serviceType);
        var method = service.Methods.Single(candidate => candidate.Name == "EchoAsync");

        Assert.Equal(0, method.Parameters[0].DefaultValue);
        Assert.Equal(Enum.ToObject(statusType, 0), method.Parameters[1].DefaultValue);
        Assert.Equal(Guid.Empty, method.Parameters[2].DefaultValue);
        Assert.Equal(DateTime.MinValue, method.Parameters[3].DefaultValue);
    }

    [Fact]
    public void GeneratedDefaultsPreserveOptionalAttributeParameters()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Runtime.InteropServices;
            using System.Threading.Tasks;

            namespace Metadata.OptionalDefaults
            {
                [DotBoxDService]
                public interface IOptionalDefaults
                {
                    Task<int> CountAsync([Optional] int value, [Optional] string? label);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var proxy = GeneratedSource(runResult, "DotBoxDRpcProxy");
        Assert.Contains("CountAsync(int value = default, string? label = default)", proxy);

        var asyncSibling = GeneratedSource(runResult, "DotBoxDRpcAsync");
        Assert.Contains(
            "CountAsync(int value = default, string? label = default, global::System.Threading.CancellationToken ct = default)",
            asyncSibling);

        var assembly = CompileAndLoad(source);
        var serviceType = assembly.GetType("Metadata.OptionalDefaults.IOptionalDefaults")!;
        var generated = assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<GeneratedService>>(
            generated.GetProperty("Services")!.GetValue(null));

        var service = services.Single(candidate => candidate.ServiceType == serviceType);
        var method = service.Methods.Single(candidate => candidate.Name == "CountAsync");

        Assert.True(method.Parameters[0].HasDefaultValue);
        Assert.Equal(0, method.Parameters[0].DefaultValue);
        Assert.True(method.Parameters[1].HasDefaultValue);
        Assert.Null(method.Parameters[1].DefaultValue);
    }

    [Fact]
    public void GeneratedDefaultsPreserveDateTimeConstantAttributeParameters()
    {
        const long ticks = 637135200000000000L;
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Threading.Tasks;

            namespace Metadata.DateTimeConstantDefaults
            {
                [DotBoxDService]
                public interface IDateTimeConstantDefaults
                {
                    Task<int> CountAsync([Optional, DateTimeConstant(637135200000000000L)] DateTime at);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        const string expectedParameter =
            "[global::System.Runtime.InteropServices.OptionalAttribute] " +
            "[global::System.Runtime.CompilerServices.DateTimeConstantAttribute(637135200000000000L)] " +
            "global::System.DateTime at";
        Assert.Contains(expectedParameter, GeneratedSource(runResult, "DotBoxDRpcProxy"));
        Assert.Contains(
            expectedParameter + ", global::System.Threading.CancellationToken ct = default",
            GeneratedSource(runResult, "DotBoxDRpcAsync"));

        var assembly = CompileAndLoad(source);
        var serviceType = assembly.GetType("Metadata.DateTimeConstantDefaults.IDateTimeConstantDefaults")!;
        var generated = assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<GeneratedService>>(
            generated.GetProperty("Services")!.GetValue(null));

        var service = services.Single(candidate => candidate.ServiceType == serviceType);
        var method = service.Methods.Single(candidate => candidate.Name == "CountAsync");

        Assert.True(method.Parameters[0].HasDefaultValue);
        Assert.Equal(new DateTime(ticks), method.Parameters[0].DefaultValue);
    }

    private static string GeneratedSource(GeneratorDriverRunResult runResult, string hintFragment) =>
        runResult.GeneratedTrees.First(t => t.FilePath.Contains(hintFragment)).GetText().ToString();
}
