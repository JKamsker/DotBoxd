using System.Security.Cryptography;
using System.Text;

namespace SafeIR.Tests;

/// <summary>
/// Regression tests for PAL-0019. The canonical module hasher was refactored to
/// avoid materializing per-node <see cref="List{T}"/> instances and boxed
/// enumerators while serializing nested type, call, list, and map records. The
/// refactor must keep the canonical text (and therefore the hash) byte-for-byte
/// identical, because <c>ExecutionPlanGuard</c> compares a module's stored hash
/// against a freshly rebuilt one on every execution.
/// </summary>
public sealed class Fix_PAL_0019_Tests
{
    [Fact]
    public void Serialize_preserves_byte_exact_nested_records()
    {
        var module = NestedLiteralModule();

        var serialized = CanonicalModuleHasher.Serialize(module);

        Assert.Equal(ExpectedSerialization(), serialized);
    }

    [Fact]
    public void Hash_matches_sha256_of_serialized_text()
    {
        var module = NestedLiteralModule();

        var hash = CanonicalModuleHasher.Hash(module);

        var expected = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalModuleHasher.Serialize(module))))
            .ToLowerInvariant();
        Assert.Equal(expected, hash);
    }

    [Fact]
    public void Hash_is_stable_across_repeated_calls()
    {
        var module = NestedLiteralModule();

        Assert.Equal(CanonicalModuleHasher.Hash(module), CanonicalModuleHasher.Hash(NestedLiteralModule()));
    }

    /// <summary>
    /// Builds the canonical serialization independently from the production helpers
    /// using only the unchanged <see cref="CanonicalEncoding.Record(string?[])"/>
    /// primitive, so any drift in the refactored array-building helpers is caught.
    /// </summary>
    private static string ExpectedSerialization()
    {
        // Nested generic type: List<Map<I32, String>> exercises Type() recursion.
        var mapType = CanonicalEncoding.Record(
            "type", "Map",
            CanonicalEncoding.Record("type", "I32"),
            CanonicalEncoding.Record("type", "String"));
        var listOfMapType = CanonicalEncoding.Record("type", "List", mapType);

        // List literal: List<I32> { 1, 2 } exercises ListLiteral().
        var listLiteral = CanonicalEncoding.Record(
            "list",
            CanonicalEncoding.Record("type", "I32"),
            CanonicalEncoding.Record("i32", "1"),
            CanonicalEncoding.Record("i32", "2"));

        // Map literal entries are sorted by their canonical record text.
        var entryA = CanonicalEncoding.Record(
            "entry",
            CanonicalEncoding.Record("string", "a"),
            CanonicalEncoding.Record("i32", "10"));
        var entryB = CanonicalEncoding.Record(
            "entry",
            CanonicalEncoding.Record("string", "b"),
            CanonicalEncoding.Record("i32", "20"));
        var sortedEntries = new[] { entryA, entryB };
        Array.Sort(sortedEntries, StringComparer.Ordinal);
        var mapLiteral = CanonicalEncoding.Record(
            "map",
            CanonicalEncoding.Record("type", "String"),
            CanonicalEncoding.Record("type", "I32"),
            sortedEntries[0],
            sortedEntries[1]);

        // Call with generic type argument and nested call argument exercises Call().
        var innerCall = CanonicalEncoding.Record(
            "call",
            "inner",
            null,
            CanonicalEncoding.Record("lit", listLiteral));
        var outerCall = CanonicalEncoding.Record(
            "call",
            "outer",
            listOfMapType,
            CanonicalEncoding.Record("lit", mapLiteral),
            innerCall);

        var builder = new StringBuilder();
        WriteRecord(builder, "canonicalizer", CanonicalModuleHasher.CanonicalizerVersion);
        WriteRecord(builder, "module", "pal-0019", "1.0.0", SandboxLanguage.CurrentVersion.ToString());
        WriteRecord(builder, "fn", "entry", "main", CanonicalEncoding.Record("type", "Unit"));
        WriteRecord(builder, "expr", outerCall);
        WriteRecord(builder, "return", CanonicalEncoding.Record("lit", CanonicalEncoding.Record("unit")));

        return builder.ToString();
    }

    private static void WriteRecord(StringBuilder builder, params string?[] fields)
    {
        builder.Append(CanonicalEncoding.Record(fields));
        builder.Append('\n');
    }

    private static SandboxModule NestedLiteralModule()
    {
        var span = new SourceSpan(0, 0);

        var listOfMapType = SandboxType.List(SandboxType.Map(SandboxType.I32, SandboxType.String));

        var listLiteral = new LiteralExpression(
            SandboxValue.FromList(
                [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)],
                SandboxType.I32),
            span);

        var mapLiteral = new LiteralExpression(
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("a")] = SandboxValue.FromInt32(10),
                    [SandboxValue.FromString("b")] = SandboxValue.FromInt32(20),
                },
                SandboxType.String,
                SandboxType.I32),
            span);

        var innerCall = new CallExpression("inner", [listLiteral], null, span);
        var outerCall = new CallExpression("outer", [mapLiteral, innerCall], listOfMapType, span);

        return new SandboxModule(
            "pal-0019",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.Unit,
                    [
                        new ExpressionStatement(outerCall, span),
                        new ReturnStatement(
                            new LiteralExpression(SandboxValue.Unit, span),
                            span),
                    ])
            ],
            new Dictionary<string, string>());
    }
}
