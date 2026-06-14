using System.Text.Json;
using SafeIR;

namespace SafeIR.Tests;

public sealed class JsonPathLiteralTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("/etc/passwd")]
    [InlineData("file:///etc/passwd")]
    [InlineData("C:foo")]
    [InlineData("safe.txt:ads")]
    [InlineData("config//settings.json")]
    [InlineData("./config/settings.json")]
    [InlineData("sub/../config/settings.json")]
    [InlineData("config/./settings.json")]
    [InlineData("config/settings.json.")]
    [InlineData("config/settings.json ")]
    [InlineData("NUL")]
    [InlineData("CON.txt")]
    [InlineData("config/\u0001settings.json")]
    public void Path_literals_reject_non_portable_paths(string path)
    {
        var expression = $$"""{ "path": {{JsonSerializer.Serialize(path)}} }""";

        var ex = Assert.Throws<SandboxValidationException>(() =>
            SafeIrJsonImporter.Import(ModuleWithReturn(expression)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-PATH");
    }

    [Theory]
    [InlineData("config/settings.json")]
    [InlineData("sub/config/settings.json")]
    public void Path_literals_allow_canonical_relative_paths(string path)
    {
        var expression = $$"""{ "path": {{JsonSerializer.Serialize(path)}} }""";

        _ = SafeIrJsonImporter.Import(ModuleWithReturn(expression));
    }

    private static string ModuleWithReturn(string returnValue)
        => $$"""
        {
          "id": "path-literals",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": {{returnValue}} }]
            }
          ]
        }
        """;
}
