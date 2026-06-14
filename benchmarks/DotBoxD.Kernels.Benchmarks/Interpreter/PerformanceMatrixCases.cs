namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal sealed record PerformanceMatrixCase(
    string Name,
    int Iterations,
    int Warmup,
    Func<int, object> Handwritten,
    string ModuleJson);

internal static class PerformanceMatrixCases
{
    public static IReadOnlyList<PerformanceMatrixCase> All()
        => [
            new("trivial no-loop", 1_000_000, 50_000, static n => n, TrivialJson()),
            new("i32 add/rem loop", 10_000_000, 250_000, HandwrittenI32Modulo, I32ModuloJson()),
            new("math.sqrt binding", 2_000_000, 100_000, HandwrittenSqrt, SqrtJson()),
            new("math.sqrt x3 binding", 1_000_000, 50_000, PerformanceMatrixMathCases.HandwrittenSqrt3, PerformanceMatrixMathCases.Sqrt3Json()),
            new("string.length binding", 1_000_000, 50_000, HandwrittenStringLength, StringLengthJson()),
            new("list.count intrinsic", 1_000_000, 50_000, HandwrittenListCount, ListCountJson()),
            new("list.get intrinsic", 1_000_000, 50_000, HandwrittenListGet, ListGetJson()),
            new("map.get intrinsic", 500_000, 25_000, HandwrittenMapGet, MapGetJson()),
            new("local function call", 1_000_000, 50_000, HandwrittenLocalCall, LocalCallJson())
        ];

    private static string TrivialJson()
        => """
        {
          "id": "matrix-trivial",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [ { "op": "return", "value": { "var": "iterations" } } ]
            }
          ]
        }
        """;

    private static object HandwrittenI32Modulo(int iterations)
    {
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total = (total + i) % 1_000_003;
        }

        return total;
    }

    private static object HandwrittenSqrt(int iterations)
    {
        var total = 1.0;
        for (var i = 0; i < iterations; i++)
        {
            total = Math.Sqrt(total);
        }

        return total;
    }

    private static object HandwrittenStringLength(int iterations)
    {
        var text = "abcdef";
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total += text.Length;
        }

        return total;
    }

    private static object HandwrittenListCount(int iterations)
    {
        var items = new[] { 1, 2, 3 };
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total += items.Length;
        }

        return total;
    }

    private static object HandwrittenListGet(int iterations)
    {
        var items = new[] { 1, 2, 3 };
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total += items[i % 3];
        }

        return total;
    }

    private static object HandwrittenMapGet(int iterations)
    {
        var scores = new Dictionary<string, int>(StringComparer.Ordinal) { ["alice"] = 7 };
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total += scores["alice"];
        }

        return total;
    }

    private static object HandwrittenLocalCall(int iterations)
    {
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total = Increment(total);
        }

        return total;
    }

    private static int Increment(int value) => value + 1;

    private static string I32ModuloJson()
        => """
        {
          "id": "matrix-i32-modulo",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                {
                  "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [
                    { "op": "set", "name": "total", "value": {
                      "op": "rem",
                      "left": { "op": "add", "left": { "var": "total" }, "right": { "var": "i" } },
                      "right": { "i32": 1000003 } } }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string SqrtJson()
        => """
        {
          "id": "matrix-sqrt",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "F64",
              "body": [
                { "op": "set", "name": "total", "value": { "f64": 1.0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [{ "op": "set", "name": "total", "value": { "call": "math.sqrt", "args": [{ "var": "total" }] } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string StringLengthJson()
        => """
        {
          "id": "matrix-string-length",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "text", "value": { "string": "abcdef" } },
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "call": "string.length", "args": [{ "var": "text" }] } } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string ListCountJson()
        => """
        {
          "id": "matrix-list-count",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "items", "value": { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }] } },
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "call": "list.count", "args": [{ "var": "items" }] } } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string ListGetJson()
        => """
        {
          "id": "matrix-list-get",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "items", "value": { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }] } },
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "call": "list.get", "args": [{ "var": "items" }, { "op": "rem", "left": { "var": "i" }, "right": { "i32": 3 } }] } } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string MapGetJson()
        => """
        {
          "id": "matrix-map-get",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "scores", "value": { "call": "map.set", "args": [{ "call": "map.empty", "genericType": { "name": "Map", "arguments": ["String", "I32"] }, "args": [] }, { "string": "alice" }, { "i32": 7 }] } },
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "call": "map.get", "args": [{ "var": "scores" }, { "string": "alice" }] } } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string LocalCallJson()
        => """
        {
          "id": "matrix-local-call",
          "version": "1.0.0",
          "functions": [
            {
              "id": "increment",
              "visibility": "private",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "op": "add", "left": { "var": "value" }, "right": { "i32": 1 } } }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [{ "op": "set", "name": "total", "value": { "call": "increment", "args": [{ "var": "total" }] } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;
}
