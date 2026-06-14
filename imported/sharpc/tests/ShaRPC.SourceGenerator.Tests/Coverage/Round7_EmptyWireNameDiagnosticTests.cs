using System.Linq;
using Microsoft.CodeAnalysis;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Round 7 regression (deferred R6 finding #7). An explicitly configured empty wire name was accepted with
/// no build-time diagnostic. <c>[ShaRpcService(Name = "")]</c> compiled but every dispatch failed at
/// runtime (the empty name never matches), and <c>[ShaRpcMethod(Name = "")]</c> threw
/// <c>ArgumentException</c> on the first call. An empty/whitespace wire name must be rejected at build time:
/// SHARPC003 for the service, SHARPC002 for the method.
/// </summary>
public sealed class Round7_EmptyWireNameDiagnosticTests
{
    [Fact]
    public void Generator_ReportsError_ForEmptyServiceWireName()
    {
        const string source = @"
using ShaRPC.Core.Attributes;
using System.Threading.Tasks;

namespace Bug.EmptyServiceName
{
    [ShaRpcService(Name = """")]
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
            d => d.Id == "SHARPC003" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_ReportsError_ForEmptyMethodWireName()
    {
        const string source = @"
using ShaRPC.Core.Attributes;
using System.Threading.Tasks;

namespace Bug.EmptyMethodName
{
    [ShaRpcService]
    public interface IEmptyMethod
    {
        [ShaRpcMethod(Name = """")]
        Task<int> GetAsync();
    }
}";
        var runResult = GeneratorTestHelper.CreateDriver()
            .RunGenerators(GeneratorTestHelper.CreateCompilation(source))
            .GetRunResult();

        Assert.Contains(
            runResult.Diagnostics,
            d => d.Id == "SHARPC002" && d.Severity == DiagnosticSeverity.Error);
    }
}
