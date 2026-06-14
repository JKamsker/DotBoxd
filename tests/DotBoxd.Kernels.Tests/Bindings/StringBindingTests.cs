using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class StringBindingTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Default_string_primitives_execute(ExecutionMode mode, bool compiler)
    {
        var length = await ExecuteReturnAsync(
            """{ "call": "string.length", "args": [{ "string": "abcdef" }] }""",
            "I32",
            SandboxPolicyBuilder.Create().Build(),
            Options(mode),
            compiler);
        var substring = await ExecuteReturnAsync(
            """
            {
              "call": "string.substringBudgeted",
              "args": [{ "string": "abcdef" }, { "i32": 1 }, { "i32": 3 }]
            }
            """,
            "String",
            SandboxPolicyBuilder.Create().Build(),
            Options(mode),
            compiler);
        var isEmpty = await ExecuteReturnAsync(
            """{ "call": "string.isEmpty", "args": [{ "string": "" }] }""",
            "Bool",
            SandboxPolicyBuilder.Create().Build(),
            Options(mode),
            compiler);
        var notEmpty = await ExecuteReturnAsync(
            """{ "call": "string.isEmpty", "args": [{ "string": "safe" }] }""",
            "Bool",
            SandboxPolicyBuilder.Create().Build(),
            Options(mode),
            compiler);
        var equals = await ExecuteReturnAsync(
            """
            {
              "call": "string.equals",
              "args": [{ "string": "safe" }, { "string": "safe" }]
            }
            """,
            "Bool",
            SandboxPolicyBuilder.Create().Build(),
            Options(mode),
            compiler);
        var notEquals = await ExecuteReturnAsync(
            """
            {
              "call": "string.equals",
              "args": [{ "string": "safe" }, { "string": "SAFE" }]
            }
            """,
            "Bool",
            SandboxPolicyBuilder.Create().Build(),
            Options(mode),
            compiler);
        var compare = await ExecuteReturnAsync(
            """
            {
              "call": "string.compareOrdinal",
              "args": [{ "string": "abc" }, { "string": "abd" }]
            }
            """,
            "I32",
            SandboxPolicyBuilder.Create().Build(),
            Options(mode),
            compiler);

        AssertSucceeded(length, mode);
        Assert.Equal(6, ((I32Value)length.Value!).Value);
        Assert.Equal(1, length.ResourceUsage.HostCalls);
        AssertSucceeded(substring, mode);
        Assert.Equal("bcd", ((StringValue)substring.Value!).Value);
        AssertSucceeded(isEmpty, mode);
        Assert.True(((BoolValue)isEmpty.Value!).Value);
        AssertSucceeded(notEmpty, mode);
        Assert.False(((BoolValue)notEmpty.Value!).Value);
        AssertSucceeded(equals, mode);
        Assert.True(((BoolValue)equals.Value!).Value);
        AssertSucceeded(notEquals, mode);
        Assert.False(((BoolValue)notEquals.Value!).Value);
        AssertSucceeded(compare, mode);
        Assert.Equal(-1, ((I32Value)compare.Value!).Value);
    }

    [Theory]
    [InlineData("abc", "abc", 0, ExecutionMode.Interpreted, false)]
    [InlineData("abd", "abc", 1, ExecutionMode.Interpreted, false)]
    [InlineData("abc", "abc", 0, ExecutionMode.Compiled, true)]
    [InlineData("abd", "abc", 1, ExecutionMode.Compiled, true)]
    public async Task Compare_ordinal_returns_normalized_result(
        string left,
        string right,
        int expected,
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteReturnAsync(
            $$"""
            {
              "call": "string.compareOrdinal",
              "args": [{ "string": "{{left}}" }, { "string": "{{right}}" }]
            }
            """,
            "I32",
            SandboxPolicyBuilder.Create().Build(),
            Options(mode),
            compiler);

        AssertSucceeded(result, mode);
        Assert.Equal(expected, ((I32Value)result.Value!).Value);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Budgeted_substring_charges_string_budget(ExecutionMode mode, bool compiler)
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "string.substringBudgeted",
              "args": [{ "string": "abcde" }, { "i32": 1 }, { "i32": 2 }]
            }
            """,
            "String",
            SandboxPolicyBuilder.Create().WithMaxTotalStringBytes(12).Build(),
            Options(mode),
            compiler);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(-1, 1, ExecutionMode.Interpreted, false)]
    [InlineData(1, -1, ExecutionMode.Interpreted, false)]
    [InlineData(4, 1, ExecutionMode.Interpreted, false)]
    [InlineData(2, 3, ExecutionMode.Interpreted, false)]
    [InlineData(-1, 1, ExecutionMode.Compiled, true)]
    [InlineData(2, 3, ExecutionMode.Compiled, true)]
    public async Task Substring_rejects_invalid_ranges(
        int startIndex,
        int length,
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteReturnAsync(
            $$"""
            {
              "call": "string.substringBudgeted",
              "args": [{ "string": "abcd" }, { "i32": {{startIndex}} }, { "i32": {{length}} }]
            }
            """,
            "String",
            SandboxPolicyBuilder.Create().Build(),
            Options(mode),
            compiler);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_string_length_enforces_host_call_limit()
    {
        var result = await ExecuteReturnAsync(
            """{ "call": "string.length", "args": [{ "string": "abcdef" }] }""",
            "I32",
            SandboxPolicyBuilder.Create().WithMaxHostCalls(0).Build(),
            Options(ExecutionMode.Compiled),
            compiler: true);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    private static SandboxExecutionOptions Options(ExecutionMode mode)
        => new() { Mode = mode, AllowFallbackToInterpreter = false };

    private static async Task<SandboxExecutionResult> ExecuteReturnAsync(
        string expression,
        string returnType,
        SandboxPolicy policy,
        SandboxExecutionOptions options,
        bool compiler = false)
    {
        var host = SandboxTestHost.Create(compiler: compiler);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "string-bindings",
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
        """);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
    }

    private static void AssertSucceeded(SandboxExecutionResult result, ExecutionMode mode)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(mode, result.ActualMode);
    }
}
