using FluentAssertions;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public partial class CodegenRegressionTests
{
    [Fact]
    public void FloatingPointSpecialDefaults_ArePreservedInGeneratedSurface()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.FloatDefaults
            {
                [DotBoxDService]
                public interface IFloatDefaults
                {
                    Task<double> MeasureAsync(
                        double value = double.NaN,
                        float floor = float.NegativeInfinity,
                        double ceiling = double.PositiveInfinity);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.FloatDefaults", "IFloatDefaults", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain(
            "MeasureAsync(double value = global::System.Double.NaN, float floor = global::System.Single.NegativeInfinity, double ceiling = global::System.Double.PositiveInfinity)");
        proxy.Should().Contain(
            "MeasureAsync(double value = global::System.Double.NaN, float floor = global::System.Single.NegativeInfinity, double ceiling = global::System.Double.PositiveInfinity, global::System.Threading.CancellationToken ct = default)");

        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("IFloatDefaults.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();
        asyncSibling.Should().Contain(
            "MeasureAsync(double value = global::System.Double.NaN, float floor = global::System.Single.NegativeInfinity, double ceiling = global::System.Double.PositiveInfinity, global::System.Threading.CancellationToken ct = default)");

        var factory = generated
            .Single(g => g.HintName == "DotBoxDGenerated.g.cs")
            .SourceText.ToString();
        factory.Should().Contain("global::System.Double.NaN),");
        factory.Should().Contain("global::System.Single.NegativeInfinity),");
        factory.Should().Contain("global::System.Double.PositiveInfinity),");
    }

    [Fact]
    public void ParamsParameters_ArePreservedInGeneratedProxySurface()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ParamsSurface
            {
                [DotBoxDService]
                public interface IParamsSurface
                {
                    Task SubmitAsync(params string[] names);
                    int Sum(params int[] values);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.ParamsSurface", "IParamsSurface", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("SubmitAsync(params string[] names)");
        proxy.Should().Contain("Sum(params int[] values)");

        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("IParamsSurface.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();
        asyncSibling.Should().Contain(
            "SubmitAsync(string[] names, global::System.Threading.CancellationToken ct = default)");
        asyncSibling.Should().Contain(
            "SumAsync(int[] values, global::System.Threading.CancellationToken ct = default)");
    }

    [Fact]
    public void CallerInfoAttributes_ArePreservedInGeneratedServiceSurface()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;

            namespace Regress.CallerInfoSurface
            {
                [DotBoxDService]
                public interface ICallerInfoSurface
                {
                    Task TraceAsync(
                        string value,
                        [CallerMemberName] string member = "",
                        [CallerFilePath] string file = "",
                        [CallerLineNumber] int line = 0,
                        [CallerArgumentExpression("value")] string expression = "");
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        const string callerInfoParameters =
            "string value, " +
            "[global::System.Runtime.CompilerServices.CallerMemberNameAttribute] string member = \"\", " +
            "[global::System.Runtime.CompilerServices.CallerFilePathAttribute] string @file = \"\", " +
            "[global::System.Runtime.CompilerServices.CallerLineNumberAttribute] int line = 0, " +
            "[global::System.Runtime.CompilerServices.CallerArgumentExpressionAttribute(\"value\")] string expression = \"\"";

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.CallerInfoSurface", "ICallerInfoSurface", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("TraceAsync(" + callerInfoParameters + ")");

        var asyncSibling = generated
            .Single(g => g.HintName.EndsWith("ICallerInfoSurface.DotBoxDRpcAsync.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();
        asyncSibling.Should().Contain(
            "TraceAsync(" + callerInfoParameters + ", global::System.Threading.CancellationToken ct = default)");
    }
}
