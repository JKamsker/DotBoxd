namespace SafeIR.Tests;

public sealed class ImportedValidationTests
{
    [Fact]
    public async Task Valid_for_range_program_prepares_and_executes()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "sum-to-n",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "exclusiveEnd", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "sum", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 1 },
                  "end": { "var": "exclusiveEnd" },
                  "body": [
                    {
                      "op": "set",
                      "name": "sum",
                      "value": { "op": "add", "left": { "var": "sum" }, "right": { "var": "i" } }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "sum" } }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt32(11));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(55, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task Validation_aggregates_unknown_call_and_missing_return()
    {
        var ex = await PrepareFailsAsync("""
        {
          "id": "multiple-errors",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "expr", "value": { "call": "host.nope", "args": [] } }
              ]
            }
          ]
        }
        """);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-UNKNOWN");
        Assert.Contains(ex.Diagnostics, d => d.Code == "E-FN-RETURN");
    }

    [Fact]
    public async Task Unknown_binding_call_is_rejected()
    {
        var ex = await PrepareFailsAsync(ModuleReturning(
            """{ "call": "host.evil", "args": [] }""",
            "I32"));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-UNKNOWN");
    }

    [Fact]
    public async Task Call_arity_mismatch_is_rejected()
    {
        var ex = await PrepareFailsAsync(ModuleReturning(
            """{ "call": "math.abs", "args": [] }""",
            "I32"));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-ARITY");
    }

    [Fact]
    public async Task Call_argument_type_mismatch_is_rejected()
    {
        var ex = await PrepareFailsAsync(ModuleReturning(
            """{ "call": "math.abs", "args": [{ "bool": true }] }""",
            "I32"));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-MISMATCH");
    }

    [Fact]
    public async Task Read_before_assignment_is_rejected()
    {
        var ex = await PrepareFailsAsync(ModuleReturning("""{ "var": "missing" }""", "I32"));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-LOCAL-UNKNOWN");
    }

    [Fact]
    public async Task Return_type_mismatch_is_rejected()
    {
        var ex = await PrepareFailsAsync(ModuleReturning("""{ "bool": true }""", "I32"));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-MISMATCH");
    }

    [Fact]
    public async Task Arithmetic_type_mismatch_is_rejected()
    {
        var ex = await PrepareFailsAsync(ModuleReturning(
            """{ "op": "add", "left": { "i32": 1 }, "right": { "f64": 2.0 } }""",
            "I32"));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-MISMATCH");
    }

    [Fact]
    public async Task Recursive_function_call_is_rejected()
    {
        var ex = await PrepareFailsAsync("""
        {
          "id": "recursive-call",
          "version": "1.0.0",
          "functions": [
            {
              "id": "loop",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "op": "add", "left": { "call": "loop", "args": [] }, "right": { "i32": 1 } }
                }
              ]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "loop", "args": [] } }]
            }
          ]
        }
        """);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CALL-RECURSION");
    }

    private static async Task<SandboxValidationException> PrepareFailsAsync(string json)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(json);
        return await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));
    }

    private static string ModuleReturning(string expression, string returnType)
        => $$"""
        {
          "id": "validation-import",
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
}
