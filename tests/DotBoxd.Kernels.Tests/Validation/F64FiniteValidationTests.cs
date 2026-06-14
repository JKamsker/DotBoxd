namespace DotBoxd.Kernels.Tests;

public sealed class F64FiniteValidationTests
{
    [Fact]
    public void Json_f64_literal_rejects_non_finite_overflow()
    {
        var ex = Assert.Throws<SandboxValidationException>(() =>
            DotBoxdJsonImporter.Import(MinimalModule("""{ "f64": 1e309 }""")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-TYPE");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void F64_factory_rejects_non_finite_values(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SandboxValue.FromDouble(value));
    }

    [Fact]
    public async Task Programmatic_f64_literal_rejects_non_finite_value()
    {
        var host = SandboxTestHost.Create();
        var module = new SandboxModule(
            "non-finite-f64",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.F64,
                    [
                        new ReturnStatement(
                            new LiteralExpression(new F64Value(double.NaN), new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CONST-F64");
    }

    private static string MinimalModule(string returnValue)
        => $$"""
        {
          "id": "f64-finite-validation",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "F64",
              "body": [{ "op": "return", "value": {{returnValue}} }]
            }
          ]
        }
        """;
}
