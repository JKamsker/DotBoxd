namespace DotBoxd.Kernels.Benchmarks.Interpreter;

internal static class PerformanceMatrixMathCases
{
    public static object HandwrittenSqrt3(int iterations)
    {
        var total = 1.0;
        for (var i = 0; i < iterations; i++)
        {
            total = Math.Sqrt(Math.Sqrt(Math.Sqrt(total)));
        }

        return total;
    }

    public static string Sqrt3Json()
        => """
        {
          "id": "matrix-sqrt-x3",
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
                  "body": [{ "op": "set", "name": "total", "value": {
                    "call": "math.sqrt",
                    "args": [{
                      "call": "math.sqrt",
                      "args": [{
                        "call": "math.sqrt",
                        "args": [{ "var": "total" }]
                      }]
                    }]
                  } }] },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;
}
