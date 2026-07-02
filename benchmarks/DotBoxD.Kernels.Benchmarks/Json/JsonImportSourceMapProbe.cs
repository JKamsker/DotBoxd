namespace DotBoxD.Kernels.Benchmarks.Json;

using System.Diagnostics;
using System.Text;
using DotBoxD.Kernels.Serialization.Json;

internal static class JsonImportSourceMapProbe
{
    private const int StatementCount = 250;
    private const int Warmup = 20;
    private const int Iterations = 500;
    private static int _sink;

    public static void Run()
    {
        var moduleJson = BuildModuleJson(StatementCount);
        _ = Measure(moduleJson, Warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Write("Import module with source spans", Measure(moduleJson, Iterations));
        GC.KeepAlive(_sink);
    }

    private static Measurement Measure(string moduleJson, int iterations)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var module = JsonImporter.Import(moduleJson);
            _sink += module.Functions.Count;
        }

        watch.Stop();
        return new Measurement(watch.Elapsed, GC.GetAllocatedBytesForCurrentThread() - before, iterations);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name}: {measurement.Elapsed.TotalMilliseconds:N1} ms, " +
            $"{measurement.Elapsed.TotalNanoseconds / measurement.Iterations:N1} ns/op, " +
            $"{measurement.Allocated:N0} B, " +
            $"{(double)measurement.Allocated / measurement.Iterations:N1} B/op");

    private static string BuildModuleJson(int statementCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("""  "id": "json-import-source-map-probe",""");
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
            builder.Append("""        { "op": "set", "name": "v""");
            builder.Append(i);
            builder.Append("\", \"value\": { \"i32\": ");
            builder.Append(i);
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

    private readonly record struct Measurement(TimeSpan Elapsed, long Allocated, int Iterations);
}
