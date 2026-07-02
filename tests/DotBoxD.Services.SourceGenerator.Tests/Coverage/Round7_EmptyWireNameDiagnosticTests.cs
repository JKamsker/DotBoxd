using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

/// <summary>
/// Round 7 regression (deferred R6 finding #7). An explicitly configured empty wire name was accepted with
/// no build-time diagnostic. <c>[DotBoxDService(Name = "")]</c> compiled but every dispatch failed at
/// runtime (the empty name never matches), and <c>[DotBoxDMethod(Name = "")]</c> threw
/// <c>ArgumentException</c> on the first call. An empty/whitespace wire name must be rejected at build time:
/// DBXS003 for the service, DBXS002 for the method.
/// </summary>
public sealed class Round7_EmptyWireNameDiagnosticTests
{
    [Fact]
    public void Generator_ReportsError_ForEmptyServiceWireName()
    {
        const string source = @"
using DotBoxD.Services.Attributes;
using System.Threading.Tasks;

namespace Bug.EmptyServiceName
{
    [DotBoxDService(Name = """")]
    public interface IEmptyName
    {
        Task<int> GetAsync();
    }
}";
        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS003" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_ReportsError_ForEmptyMethodWireName()
    {
        const string source = @"
using DotBoxD.Services.Attributes;
using System.Threading.Tasks;

namespace Bug.EmptyMethodName
{
    [DotBoxDService]
    public interface IEmptyMethod
    {
        [DotBoxDMethod(Name = """")]
        Task<int> GetAsync();
    }
}";
        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS002" && d.Severity == DiagnosticSeverity.Error);
    }
}

public sealed class RouteNameBudgetDiagnosticTests
{
    [Fact]
    public void Generator_ReportsError_ForOversizedCustomServiceWireName()
    {
        var source = $$"""
            using DotBoxD.Services.Attributes;

            namespace Bug.RouteNameBudget
            {
                [DotBoxDService(Name = "{{LongAsciiName()}}")]
                public interface ICustomServiceName
                {
                    int Get();
                }
            }
            """;

        var runResult = Run(source);

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS003" &&
                d.Severity == DiagnosticSeverity.Error &&
                d.GetMessage().Contains("ServiceName limit is 256 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_ReportsError_ForOversizedDefaultServiceWireName()
    {
        var interfaceName = "I" + new string('A', 256);
        var source = $$"""
            using DotBoxD.Services.Attributes;

            namespace Bug.RouteNameBudget
            {
                [DotBoxDService]
                public interface {{interfaceName}}
                {
                    int Get();
                }
            }
            """;

        var runResult = Run(source);

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS003" &&
                d.Severity == DiagnosticSeverity.Error &&
                d.GetMessage().Contains("ServiceName limit is 256 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_ReportsError_ForOversizedCustomMethodWireName()
    {
        var source = $$"""
            using DotBoxD.Services.Attributes;

            namespace Bug.RouteNameBudget
            {
                [DotBoxDService]
                public interface ICustomMethodName
                {
                    [DotBoxDMethod(Name = "{{LongAsciiName()}}")]
                    int Get();
                }
            }
            """;

        var runResult = Run(source);

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS002" &&
                d.Severity == DiagnosticSeverity.Error &&
                d.GetMessage().Contains("MethodName limit is 256 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_CountsUtf8Bytes_ForCustomMethodWireName()
    {
        var source = $$"""
            using DotBoxD.Services.Attributes;

            namespace Bug.RouteNameBudget
            {
                [DotBoxDService]
                public interface IUtf8MethodName
                {
                    [DotBoxDMethod(Name = "{{new string('é', 129)}}")]
                    int Get();
                }
            }
            """;

        var runResult = Run(source);

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS002" &&
                d.Severity == DiagnosticSeverity.Error &&
                d.GetMessage().Contains("258 UTF-8 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_ReportsError_ForOversizedDefaultMethodWireName()
    {
        var methodName = "M" + new string('A', 256);
        var source = $$"""
            using DotBoxD.Services.Attributes;

            namespace Bug.RouteNameBudget
            {
                [DotBoxDService]
                public interface IDefaultMethodName
                {
                    int {{methodName}}();
                }
            }
            """;

        var runResult = Run(source);

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "DBXS002" &&
                d.Severity == DiagnosticSeverity.Error &&
                d.GetMessage().Contains("MethodName limit is 256 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_AllowsRouteNamesAtUtf8ByteLimit()
    {
        var source = $$"""
            using DotBoxD.Services.Attributes;

            namespace Bug.RouteNameBudget
            {
                [DotBoxDService(Name = "{{new string('S', 256)}}")]
                public interface IAtLimit
                {
                    [DotBoxDMethod(Name = "{{new string('M', 256)}}")]
                    int Get();
                }
            }
            """;

        var runResult = Run(source);

        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id is "DBXS002" or "DBXS003");
    }

    private static Microsoft.CodeAnalysis.GeneratorDriverRunResult Run(string source) =>
        GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

    private static string LongAsciiName() => new('A', 257);
}
