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
}
