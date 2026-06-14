using System.Text;

namespace DotBoxd.Kernels.Tests;

public sealed class JsonImporterBudgetTests
{
    [Fact]
    public void Import_rejects_json_over_byte_budget_before_materialization()
    {
        var json = new string(' ', 1_048_577);

        var ex = Assert.Throws<SandboxValidationException>(() => DotBoxdJsonImporter.Import(json));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Fact]
    public void Import_rejects_string_over_byte_budget_before_materialization()
    {
        var id = new string('a', 65_537);

        var ex = Assert.Throws<SandboxValidationException>(() => DotBoxdJsonImporter.Import(MinimalModule(id)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Fact]
    public void Import_rejects_array_breadth_before_materialization()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 10_001; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append("""{ "id": "cap" }""");
        }

        var ex = Assert.Throws<SandboxValidationException>(() => DotBoxdJsonImporter.Import($$"""
        {
          "id": "wide",
          "version": "1.0.0",
          "capabilityRequests": [{{builder}}],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Fact]
    public void Import_rejects_object_breadth_before_materialization()
    {
        var builder = new StringBuilder();
        builder.Append('{');
        for (var i = 0; i < 10_001; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append('"').Append('p').Append(i).Append("\":0");
        }

        builder.Append('}');

        var ex = Assert.Throws<SandboxValidationException>(() => DotBoxdJsonImporter.Import(builder.ToString()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Fact]
    public void Import_rejects_depth_before_materialization()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 65; i++)
        {
            builder.Append('[');
        }

        for (var i = 0; i < 65; i++)
        {
            builder.Append(']');
        }

        var ex = Assert.Throws<SandboxValidationException>(() => DotBoxdJsonImporter.Import(builder.ToString()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Fact]
    public void Import_rejects_token_count_before_materialization()
    {
        var builder = new StringBuilder("[");
        for (var i = 0; i < 10_000; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append("[0,0,0,0,0,0,0,0,0,0]");
        }

        builder.Append(']');

        var ex = Assert.Throws<SandboxValidationException>(() => DotBoxdJsonImporter.Import(builder.ToString()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    [Fact]
    public void Import_rejects_aggregate_string_bytes_before_materialization()
    {
        const int strings = 9;
        var payload = new string('a', 65_000);
        var builder = new StringBuilder();
        for (var i = 0; i < strings; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append('"').Append('k').Append(i).Append("\":\"").Append(payload).Append('"');
        }

        var ex = Assert.Throws<SandboxValidationException>(() => DotBoxdJsonImporter.Import($$"""
        {
          "id": "aggregate-strings",
          "version": "1.0.0",
          "metadata": { {{builder}} },
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-LIMIT");
    }

    private static string MinimalModule(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """;
}
