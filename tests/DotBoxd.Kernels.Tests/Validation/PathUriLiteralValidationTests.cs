using System.Text.Json;

namespace DotBoxd.Kernels.Tests;

public sealed class PathUriLiteralValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("https://user:pass@example.com/config")]
    [InlineData(@"\\server\share\config")]
    public void Uri_literals_reject_invalid_json_values(string uri)
    {
        var expression = $$"""{ "uri": {{JsonSerializer.Serialize(uri)}} }""";

        var ex = Assert.Throws<SandboxValidationException>(() =>
            DotBoxdJsonImporter.Import(ModuleWithReturn("SandboxUri", expression)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-URI");
    }

    [Fact]
    public void Uri_literals_allow_absolute_values_without_user_info()
    {
        _ = DotBoxdJsonImporter.Import(ModuleWithReturn("SandboxUri", """{ "uri": "https://api.example.com/config" }"""));
    }

    [Theory]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("file:///etc/passwd")]
    [InlineData("../secret.txt")]
    [InlineData("config/../../secret.txt")]
    [InlineData("./config/settings.json")]
    [InlineData("NUL")]
    [InlineData("CON.txt")]
    [InlineData("config/name.")]
    [InlineData("config/name ")]
    [InlineData("config/\u0001settings.json")]
    public void Path_factory_rejects_non_portable_values(string path)
    {
        Assert.Throws<ArgumentException>(() => SandboxValue.FromPath(path));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("config/../../secret.txt")]
    [InlineData("NUL")]
    [InlineData("config/\u0001settings.json")]
    public void Path_literals_reject_invalid_json_values(string path)
    {
        var expression = $$"""{ "path": {{JsonSerializer.Serialize(path)}} }""";

        var ex = Assert.Throws<SandboxValidationException>(() =>
            DotBoxdJsonImporter.Import(ModuleWithReturn("SandboxPath", expression)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-PATH");
    }

    [Theory]
    [InlineData("not-a-uri")]
    [InlineData("https://user:pass@example.com/config")]
    public void Uri_factory_rejects_invalid_values(string uri)
    {
        Assert.Throws<ArgumentException>(() => SandboxValue.FromUri(uri));
    }

    [Theory]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("../secret.txt")]
    [InlineData("NUL")]
    [InlineData("config/name.")]
    [InlineData("config/\u0001settings.json")]
    public async Task Programmatic_path_literal_rejects_non_portable_value(string path)
    {
        var module = ModuleWithValue(
            SandboxType.SandboxPath,
            new SandboxPathValue(new SandboxPath(path)));

        var ex = await PrepareThrowsAsync(module);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CONST-PATH");
    }

    [Fact]
    public async Task Programmatic_uri_literal_rejects_user_info()
    {
        var module = ModuleWithValue(
            SandboxType.Scalar("SandboxUri"),
            new SandboxUriValue(new SandboxUri("https://user:pass@example.com/config")));

        var ex = await PrepareThrowsAsync(module);

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-CONST-URI");
    }

    [Fact]
    public async Task Path_literals_count_toward_string_byte_quota()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleWithReturn("SandboxPath", """{ "path": "config/settings.json" }"""));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithMaxTotalStringBytes(4).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    private static async Task<SandboxValidationException> PrepareThrowsAsync(SandboxModule module)
    {
        var host = SandboxTestHost.Create();
        return await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));
    }

    private static SandboxModule ModuleWithValue(SandboxType returnType, SandboxValue value)
        => new(
            "path-uri-model-validation",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    returnType,
                    [
                        new ReturnStatement(
                            new LiteralExpression(value, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private static string ModuleWithReturn(string returnType, string returnValue)
        => $$"""
        {
          "id": "path-uri-literal-validation",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{returnValue}} }]
            }
          ]
        }
        """;
}
