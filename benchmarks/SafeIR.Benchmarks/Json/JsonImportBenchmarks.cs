namespace SafeIR.Benchmarks.Json;

using System.Text;
using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class JsonImportBenchmarks
{
    private string _json = string.Empty;

    [Params(100, 1_000, 10_000)]
    public int StatementCount { get; set; }

    [Params(false, true)]
    public bool DuplicateLiterals { get; set; }

    [GlobalSetup]
    public void Setup()
        => _json = BuildModuleJson(StatementCount, DuplicateLiterals);

    [Benchmark]
    public SandboxModule Import()
        => SafeIrJsonImporter.Import(_json);

    private static string BuildModuleJson(int statementCount, bool duplicateLiterals)
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("""  "id": "json-import-benchmark",""");
        builder.AppendLine("""  "version": "1.0.0",""");
        builder.AppendLine("""  "functions": [""");
        builder.AppendLine("    {");
        builder.AppendLine("""      "id": "main",""");
        builder.AppendLine("""      "visibility": "entrypoint",""");
        builder.AppendLine("""      "parameters": [],""");
        builder.AppendLine("""      "returnType": "I32",""");
        builder.AppendLine("""      "body": [""");

        for (var i = 0; i < statementCount; i++)
        {
            var value = duplicateLiterals ? 1 : i;
            builder.Append("""        { "op": "set", "name": "v""");
            builder.Append(i);
            builder.Append("\", \"value\": { \"i32\": ");
            builder.Append(value);
            builder.AppendLine(""" } },""");
        }

        builder.Append("""        { "op": "return", "value": { "var": "v""");
        builder.Append(statementCount - 1);
        builder.AppendLine("\" } }");
        builder.AppendLine("      ]");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");
        return builder.ToString();
    }
}
