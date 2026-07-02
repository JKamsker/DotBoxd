using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

public sealed class ServiceDefaultValueEmitterMatrixTests
{
    public static TheoryData<string, string, string> ServiceDefaultCases { get; } = new()
    {
        { "primitive", "int amount = 7", "int amount = 7" },
        { "nullable", "int? maybe = null", "int? maybe = null" },
        { "enum", "Mode mode = Mode.Slow", "global::Matrix.Service.Mode mode = (global::Matrix.Service.Mode)2" },
        {
            "datetime-metadata",
            "[Optional, DateTimeConstant(0L)] DateTime when",
            "global::System.DateTime @when = default"
        },
        { "decimal", "decimal price = 1.5m", "decimal price = 1.5M" },
        {
            "optional-metadata-before-required",
            "[Optional] int optional, int required",
            "[global::System.Runtime.InteropServices.OptionalAttribute] int optional, int @required"
        },
        {
            "default-attribute-before-required",
            "[Optional, DefaultParameterValue(42)] int optional, int required",
            "[global::System.Runtime.InteropServices.DefaultParameterValueAttribute(42)] int optional, int @required"
        },
    };

    [Theory]
    [MemberData(nameof(ServiceDefaultCases))]
    public void Service_proxy_and_async_sibling_delegate_to_shared_default_emitter(
        string name,
        string parameters,
        string expectedParameter)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source(parameters));
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(expectedParameter, GeneratedSource(runResult, "DotBoxDRpcProxy"), StringComparison.Ordinal);
        Assert.Contains(expectedParameter, GeneratedSource(runResult, "DotBoxDRpcAsync"), StringComparison.Ordinal);

        var output = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(
            emit.Success,
            name + Environment.NewLine + string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
    }

    private static string Source(string parameters)
        => $$"""
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Threading.Tasks;
            using DotBoxD.Services.Attributes;

            namespace Matrix.Service;

            public enum Mode
            {
                Fast = 1,
                Slow = 2
            }

            [DotBoxDService]
            public interface IDefaults
            {
                Task<int> EchoAsync({{parameters}});
            }
            """;

    private static string GeneratedSource(GeneratorDriverRunResult runResult, string hintFragment)
        => runResult.GeneratedTrees
            .First(tree => tree.FilePath.Contains(hintFragment, StringComparison.Ordinal))
            .GetText()
            .ToString();
}
