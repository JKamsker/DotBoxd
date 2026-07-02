using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Serialization;

public sealed class JsonExporterStringLiteralTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public void Export_round_trips_string_literals_without_utf16_replacement()
    {
        const string validSurrogatePair = "prefix-\uD83D\uDE00-suffix";
        Assert.Equal(validSurrogatePair, RoundTripStringLiteral(validSurrogatePair));

        const string malformedUtf16 = "prefix-\uD800-suffix";
        var module = ModuleReturningString(malformedUtf16);
        string? json = null;
        var exportError = Record.Exception(() => json = JsonExporter.Export(module));
        if (exportError is not null)
        {
            Assert.True(
                exportError.Message.Contains("UTF-16", StringComparison.OrdinalIgnoreCase) ||
                exportError.Message.Contains("surrogate", StringComparison.OrdinalIgnoreCase),
                $"Expected export to name malformed UTF-16 or surrogate text, but got: {exportError.Message}");
            return;
        }

        var roundTrip = JsonImporter.Import(json!);
        Assert.Equal(malformedUtf16, StringLiteral(roundTrip));
    }

    [Fact]
    public void Export_round_trips_metadata_strings_without_utf16_replacement()
    {
        const string validSurrogatePair = "prefix-\uD83D\uDE00-suffix";
        Assert.Equal(validSurrogatePair, RoundTripMetadataValue(validSurrogatePair));

        const string malformedUtf16 = "prefix-\uD800-suffix";
        var module = ModuleWithMetadata(malformedUtf16);
        string? json = null;
        var exportError = Record.Exception(() => json = JsonExporter.Export(module));
        if (exportError is not null)
        {
            Assert.True(
                exportError.Message.Contains("UTF-16", StringComparison.OrdinalIgnoreCase) ||
                exportError.Message.Contains("surrogate", StringComparison.OrdinalIgnoreCase),
                $"Expected export to name malformed UTF-16 or surrogate text, but got: {exportError.Message}");
            return;
        }

        var roundTrip = JsonImporter.Import(json!);
        Assert.Equal(malformedUtf16, roundTrip.Metadata["description"]);
    }

    private static string RoundTripStringLiteral(string value)
        => StringLiteral(JsonImporter.Import(JsonExporter.Export(ModuleReturningString(value))));

    private static string RoundTripMetadataValue(string value)
        => JsonImporter.Import(JsonExporter.Export(ModuleWithMetadata(value))).Metadata["description"];

    private static SandboxModule ModuleReturningString(string value)
        => new(
            "string-literal-exporter",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "StringLiteral",
                    true,
                    [],
                    SandboxType.String,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.FromString(value), Span), Span)])
            ],
            new Dictionary<string, string>());

    private static SandboxModule ModuleWithMetadata(string value)
        => new(
            "metadata-exporter",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "Unit",
                    true,
                    [],
                    SandboxType.Unit,
                    [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)])
            ],
            new Dictionary<string, string>
            {
                ["description"] = value
            });

    private static string StringLiteral(SandboxModule module)
    {
        var ret = Assert.IsType<ReturnStatement>(Assert.Single(Assert.Single(module.Functions).Body));
        return Assert.IsType<StringValue>(((LiteralExpression)ret.Value).Value).Value;
    }
}
