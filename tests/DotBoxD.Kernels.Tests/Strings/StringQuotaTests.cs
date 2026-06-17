using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Interpreter;

namespace DotBoxD.Kernels.Tests.Strings;

public sealed class StringQuotaTests
{
    [Fact]
    public async Task Interpreted_string_literal_length_limit_is_enforced()
    {
        var result = await ExecuteReturnAsync(
            """{ "string": "hello" }""",
            SandboxPolicyBuilder.Create().WithMaxStringLength(4).Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        AssertQuotaExceeded(result);
    }

    [Fact]
    public async Task Compiled_string_literal_length_limit_is_enforced()
    {
        var result = await ExecuteReturnAsync(
            """{ "string": "hello" }""",
            SandboxPolicyBuilder.Create().WithMaxStringLength(4).Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            compiler: true);

        AssertQuotaExceeded(result);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Total_string_byte_limit_counts_literals_and_concat_results()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "string.concatBudgeted",
              "args": [{ "string": "aa" }, { "string": "bb" }]
            }
            """,
            SandboxPolicyBuilder.Create().WithMaxTotalStringBytes(12).Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        AssertQuotaExceeded(result);
    }

    [Fact]
    public async Task Compiled_total_string_byte_limit_counts_literals_and_concat_results()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "string.concatBudgeted",
              "args": [{ "string": "aa" }, { "string": "bb" }]
            }
            """,
            SandboxPolicyBuilder.Create().WithMaxTotalStringBytes(12).Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            compiler: true);

        AssertQuotaExceeded(result);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Budgeted_string_concat_charges_per_byte_fuel(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "string.concatBudgeted",
              "args": [{ "string": "hello" }, { "string": "world" }]
            }
            """,
            SandboxPolicyBuilder.Create().WithFuel(20).Build(),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false },
            compiler);

        AssertQuotaExceeded(result);
        Assert.Equal(mode, result.ActualMode);
    }

    [Fact]
    public async Task Entrypoint_string_input_length_limit_is_enforced()
    {
        var result = await ExecuteInputLengthAsync(
            SandboxPolicyBuilder.Create().WithMaxStringLength(4).Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        AssertQuotaExceeded(result);
    }

    [Fact]
    public async Task Compiled_entrypoint_string_input_length_limit_is_enforced()
    {
        var result = await ExecuteInputLengthAsync(
            SandboxPolicyBuilder.Create().WithMaxStringLength(4).Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            compiler: true);

        AssertQuotaExceeded(result);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task File_read_text_is_checked_against_string_limits()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config.txt"), "hello");
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("config.txt"));
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .GrantFileRead(temp.Path, 1024)
            .WithMaxStringLength(4)
            .WithWallTime(TimeSpan.FromSeconds(2))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        AssertQuotaExceeded(result);
    }

    [Fact]
    public async Task File_read_text_at_exact_string_byte_limit_is_charged_once()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config.txt"), "hello");
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("config.txt"));
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .GrantFileRead(temp.Path, 1024)
            .WithMaxTotalStringBytes(30)
            .WithWallTime(TimeSpan.FromSeconds(2))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(30, result.ResourceUsage.StringBytes);
    }

    [Fact]
    public async Task Successful_string_run_reports_string_bytes()
    {
        var result = await ExecuteReturnAsync(
            """{ "string": "safe" }""",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(8, result.ResourceUsage.StringBytes);
    }

    [Fact]
    public void String_limits_are_part_of_policy_hash()
    {
        var first = SandboxPolicyBuilder.Create().WithMaxStringLength(1).Build();
        var second = SandboxPolicyBuilder.Create().WithMaxStringLength(2).Build();
        var third = SandboxPolicyBuilder.Create().WithMaxTotalStringBytes(2).Build();

        Assert.NotEqual(first.Hash, second.Hash);
        Assert.NotEqual(first.Hash, third.Hash);
    }

    private static async Task<SandboxExecutionResult> ExecuteReturnAsync(
        string expression,
        SandboxPolicy policy,
        SandboxExecutionOptions options,
        bool compiler = false)
    {
        var host = SandboxTestHost.Create(compiler: compiler);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "string-quotas",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "String",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
    }

    private static async Task<SandboxExecutionResult> ExecuteInputLengthAsync(
        SandboxPolicy policy,
        SandboxExecutionOptions options,
        bool compiler = false)
    {
        var host = SandboxTestHost.Create(compiler: compiler);
        var module = await host.ImportJsonAsync("""
        {
          "id": "string-input",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "input", "type": "String" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "string.length", "args": [{ "var": "input" }] } }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.FromString("hello"), options);
    }

    private static void AssertQuotaExceeded(SandboxExecutionResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
