using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Serialization;

public sealed class JsonSourceMapTests
{
    [Fact]
    public void Imported_statements_and_expressions_preserve_json_source_locations()
    {
        var json = """
        {
          "id": "source-map",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "x", "value": { "i32": 1 } },
                {
                  "op": "return",
                  "value": {
                    "op": "add",
                    "left": { "var": "x" },
                    "right": { "i32": 2 }
                  }
                }
              ]
            }
          ]
        }
        """;

        var module = JsonImporter.Import(json);
        var body = module.Functions.Single().Body;
        var set = Assert.IsType<AssignmentStatement>(body[0]);
        var ret = Assert.IsType<ReturnStatement>(body[1]);
        var add = Assert.IsType<BinaryExpression>(ret.Value);

        Assert.Equal(LineOf(json, "{ \"op\": \"set\""), set.Span.Line);
        Assert.Equal(set.Span.Line + 1, ret.Span.Line);
        Assert.Equal(LineOf(json, "\"value\": {", occurrence: 2), add.Span.Line);
    }

    [Fact]
    public async Task Interpreted_debug_trace_reports_json_source_locations()
    {
        var json = """
        {
          "id": "trace-source",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "op": "add", "left": { "i32": 1 }, "right": { "i32": 2 } } }
              ]
            }
          ]
        }
        """;
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(json);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, EnableDebugTrace = true });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var trace = result.AuditEvents.First(e =>
            e.Kind == "DebugTrace" &&
            e.Fields?["category"] == "statement" &&
            e.Fields["nodeKind"] == nameof(ReturnStatement));
        Assert.Equal(LineOf(json, "{ \"op\": \"return\""), int.Parse(trace.Fields!["sourceLine"]));
    }

    [Fact]
    public void Repeated_literal_expressions_preserve_distinct_source_locations()
    {
        var json = """
        {
          "id": "repeated-source-map",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "first", "value": { "i32": 1 } },
                { "op": "set", "name": "second", "value": { "i32": 1 } },
                { "op": "return", "value": { "var": "second" } }
              ]
            }
          ]
        }
        """;

        var module = JsonImporter.Import(json);
        var body = module.Functions.Single().Body;
        var first = Assert.IsType<AssignmentStatement>(body[0]);
        var second = Assert.IsType<AssignmentStatement>(body[1]);

        Assert.Equal(LineOf(json, "\"value\": { \"i32\": 1 }"), first.Value.Span.Line);
        Assert.Equal(LineOf(json, "\"value\": { \"i32\": 1 }", occurrence: 2), second.Value.Span.Line);
    }

    [Fact]
    public void Scalar_type_errors_report_the_scalar_json_source_location()
    {
        var json = """
        {
          "id": "bad-scalar-source",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "i32": "not-int"
                  }
                }
              ]
            }
          ]
        }
        """;

        var ex = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
        var diagnostic = Assert.Single(ex.Diagnostics);
        var span = Assert.IsType<SourceSpan>(diagnostic.Span);

        Assert.Equal("E-JSON-TYPE", diagnostic.Code);
        Assert.Equal(LineOf(json, "\"not-int\""), span.Line);
        Assert.Equal(ColumnOf(json, "\"not-int\""), span.Column);
    }

    [Fact]
    public void Scalar_value_errors_report_the_scalar_json_source_location()
    {
        var json = """
        {
          "id": "bad-path-source",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Path",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "path": "../secret.txt"
                  }
                }
              ]
            }
          ]
        }
        """;

        var ex = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
        var diagnostic = Assert.Single(ex.Diagnostics);
        var span = Assert.IsType<SourceSpan>(diagnostic.Span);

        Assert.Equal("E-JSON-PATH", diagnostic.Code);
        Assert.Equal(LineOf(json, "\"../secret.txt\""), span.Line);
        Assert.Equal(ColumnOf(json, "\"../secret.txt\""), span.Column);
    }

    private static int LineOf(string text, string marker, int occurrence = 1)
    {
        var index = -1;
        for (var i = 0; i < occurrence; i++)
        {
            index = text.IndexOf(marker, index + 1, StringComparison.Ordinal);
        }

        Assert.True(index >= 0, $"marker '{marker}' not found");
        return text[..index].Count(c => c == '\n') + 1;
    }

    private static int ColumnOf(string text, string marker, int occurrence = 1)
    {
        var index = -1;
        for (var i = 0; i < occurrence; i++)
        {
            index = text.IndexOf(marker, index + 1, StringComparison.Ordinal);
        }

        Assert.True(index >= 0, $"marker '{marker}' not found");
        var lineStart = text.LastIndexOf('\n', index);
        return index - lineStart;
    }
}
