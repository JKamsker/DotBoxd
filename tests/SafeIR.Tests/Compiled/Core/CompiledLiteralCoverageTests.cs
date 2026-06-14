namespace SafeIR.Tests;

public sealed class CompiledLiteralCoverageTests
{
    public static TheoryData<string, string> LiteralCases()
        => new()
        {
            { "Unit", """{ "unit": true }""" },
            { "I64", """{ "i64": 9223372036854775807 }""" },
            { "SandboxPath", """{ "path": "config/settings.json" }""" },
            { "SandboxUri", """{ "uri": "https://api.example.com/config" }""" },
            { "PlayerId", """{ "opaqueId": { "type": "PlayerId", "value": "player-1" } }""" }
        };

    [Theory]
    [MemberData(nameof(LiteralCases))]
    public async Task Compiled_literals_match_interpreted_literals(string returnType, string expression)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleWithReturn(returnType, expression));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().DeclareOpaqueIdType("PlayerId").WithFuel(1_000).Build());

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(interpreted.Value, compiled.Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    [Fact]
    public async Task Compiled_programmatic_list_literal_returns_value()
    {
        var literal = new ListValue(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)],
            SandboxType.I32);

        var result = await ExecuteProgrammaticLiteralAsync(
            literal,
            SandboxType.List(SandboxType.I32),
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        var list = Assert.IsType<ListValue>(result.Value);
        Assert.Equal([1, 2], list.Values.Cast<I32Value>().Select(v => v.Value).ToArray());
    }

    [Fact]
    public async Task Compiled_programmatic_map_literal_returns_value()
    {
        var literal = new MapValue(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("alice")] = SandboxValue.FromInt32(41)
            },
            SandboxType.String,
            SandboxType.I32);

        var result = await ExecuteProgrammaticLiteralAsync(
            literal,
            SandboxType.Map(SandboxType.String, SandboxType.I32),
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        var map = Assert.IsType<MapValue>(result.Value);
        Assert.Equal(41, ((I32Value)map.Values[SandboxValue.FromString("alice")]).Value);
    }

    [Fact]
    public async Task Compiled_programmatic_collection_literal_enforces_policy_limits()
    {
        var literal = new ListValue(
            [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)],
            SandboxType.I32);

        var result = await ExecuteProgrammaticLiteralAsync(
            literal,
            SandboxType.List(SandboxType.I32),
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxListLength(1)
                .Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    private static string ModuleWithReturn(string returnType, string expression)
        => $$"""
        {
          "id": "compiled-literal-coverage",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;

    private static async Task<SandboxExecutionResult> ExecuteProgrammaticLiteralAsync(
        SandboxValue literal,
        SandboxType returnType,
        SandboxPolicy policy)
    {
        var span = new SourceSpan(0, 0);
        var module = new SandboxModule(
            "compiled-programmatic-literal",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    returnType,
                    [new ReturnStatement(new LiteralExpression(literal, span), span)])
            ],
            new Dictionary<string, string>());
        var host = SandboxTestHost.Create(compiler: true);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });
    }
}
