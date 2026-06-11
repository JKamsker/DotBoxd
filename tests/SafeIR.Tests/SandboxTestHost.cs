using SafeIR.Hosting;
using SafeIR.Runtime;

namespace SafeIR.Tests;

internal static class SandboxTestHost
{
    public static SandboxHost Create(bool compiler = false, string? compilerCache = null)
        => SandboxHost.Create(builder => {
            builder.AddDefaultPureBindings();
            builder.AddFileBindings();
            builder.AddTimeBindings();
            builder.AddRandomBindings();
            builder.UseInterpreter();
            if (compilerCache is not null) {
                builder.UseCompilerCache(compilerCache);
            }

            if (compiler) {
                builder.UseCompilerIfAvailable();
            }
        });

    public static string PureScoreJson(string id = "loot-score")
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "capabilityRequests": [],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "level", "type": "I32" },
                { "name": "rarity", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "base",
                  "value": { "op": "mul", "left": { "var": "level" }, "right": { "i32": 10 } }
                },
                {
                  "op": "set",
                  "name": "bonus",
                  "value": { "op": "mul", "left": { "var": "rarity" }, "right": { "i32": 25 } }
                },
                {
                  "op": "return",
                  "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "bonus" } }
                }
              ]
            }
          ]
        }
        """;
}
