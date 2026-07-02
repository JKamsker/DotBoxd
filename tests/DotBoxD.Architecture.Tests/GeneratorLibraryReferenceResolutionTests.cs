using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Architecture.Tests;

/// <summary>
/// Completeness guard against silent source-generator drift. The three code generators
/// (Services source generator, plugin analyzer, Fody weaver) are netstandard2.0 assemblies that cannot
/// reference the runtime libraries, so every library type/member/namespace they emit or match is a raw
/// <b>string literal</b> — hoisted into constant classes, but still just text the compiler never checks.
/// This test parses all generator source, extracts every DotBoxD library FQN that appears inside a
/// string literal (from the constant classes and any still-inline site), and resolves each against the
/// shipping assemblies. Moving or renaming any referenced type, nested type, FQN-qualified member, or
/// namespace turns this red instead of silently breaking generated code in consumer projects. Because it
/// validates the literals directly, no reference can escape the net — including ones no contract test pins.
/// (Compile-checked references — <c>using</c>s, real code against shared source — are intentionally
/// excluded: the compiler already catches drift there.)
/// </summary>
public sealed class GeneratorLibraryReferenceResolutionTests
{
    private static readonly string[] GeneratorSourceRoots =
    [
        "src/CodeGeneration/DotBoxD.Services.SourceGenerator",
        "src/CodeGeneration/DotBoxD.Plugins.Analyzer",
        "src/CodeGeneration/DotBoxD.Plugins.Fody",
    ];

    // FQN prefixes that intentionally do NOT resolve to a shipping type/namespace: the generators' own
    // namespaces, the namespaces they emit generated code INTO, and the GameServer sample abstractions
    // the plugin generator names by convention rather than by referencing a shipped type.
    private static readonly string[] NonShippingPrefixes =
    [
        "DotBoxD.Services.SourceGenerator",
        "DotBoxD.Plugins.Analyzer",
        "DotBoxD.Plugins.Fody",
        "DotBoxD.Plugins.Generated",
        "DotBoxD.Services.Generated",
        "DotBoxD.Kernels.Game",
    ];

    // Free-form analyzer diagnostic *category* labels: they use a namespace-like format but name no
    // shipped type/namespace, so they carry no drift risk (moving library code cannot break them).
    private static readonly HashSet<string> DiagnosticCategoryLiterals = new(StringComparer.Ordinal)
    {
        "DotBoxD.Kernels.Security",
        "DotBoxD.Kernels.Generation",
    };

    private static readonly Regex LibraryFqn =
        new(@"(?:global::)?DotBoxD(?:\.[A-Za-z_][A-Za-z0-9_]*)+", RegexOptions.Compiled);

    [Fact]
    public void Every_DotBoxD_library_reference_in_generator_source_resolves_to_a_real_symbol()
    {
        var (types, namespaces) = BuildShippingIndex();
        var root = ArchTestSupport.RepositoryRoot();
        var references = new SortedSet<string>(StringComparer.Ordinal);
        var failures = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var relRoot in GeneratorSourceRoots)
        {
            var dir = Path.Combine(root, relRoot.Replace('/', Path.DirectorySeparatorChar));
            foreach (var file in EnumerateSourceFiles(dir))
            {
                foreach (var literal in StringLiteralValues(File.ReadAllText(file)))
                {
                    foreach (Match match in LibraryFqn.Matches(literal))
                    {
                        var fqn = match.Value.StartsWith("global::", StringComparison.Ordinal)
                            ? match.Value["global::".Length..]
                            : match.Value;

                        if (IsNonShipping(fqn) || DiagnosticCategoryLiterals.Contains(fqn))
                            continue;

                        references.Add(fqn);
                        if (!Resolves(fqn, types, namespaces))
                            failures.Add($"{Path.GetFileName(file)}: '{fqn}'");
                    }
                }
            }
        }

        // Guard the scanner itself: if literal extraction silently stops matching, this floor trips
        // before a vacuous green. The live surface is ~90 distinct library FQNs across the generators.
        Assert.True(
            references.Count >= 60,
            $"Scanner found only {references.Count} distinct library references — the extractor likely regressed.");

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} DotBoxD library reference(s) in generator string literals do not resolve to a "
            + "shipping type/member/namespace. Either the library moved/renamed a symbol the generator depends "
            + "on (fix the literal/constant), or the reference is intentionally non-shipping (add its prefix to "
            + "NonShippingPrefixes):\n  " + string.Join("\n  ", failures));
    }

    private static IEnumerable<string> EnumerateSourceFiles(string dir)
        => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     // Assembly-attribute files hold assembly-name strings (InternalsVisibleTo), not emitted
                     // references; a wrong name fails at compile time, so it is not silent-drift surface.
                     && !Path.GetFileName(f).EndsWith("AssemblyInfo.cs", StringComparison.Ordinal));

    private static IEnumerable<string> StringLiteralValues(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var token in root.DescendantTokens())
        {
            switch (token.Kind())
            {
                case SyntaxKind.StringLiteralToken:               // "..." and @"..."
                case SyntaxKind.SingleLineRawStringLiteralToken:  // """..."""
                case SyntaxKind.MultiLineRawStringLiteralToken:
                case SyntaxKind.InterpolatedStringTextToken:      // literal chunks of $"..." (holes excluded)
                    yield return token.ValueText;
                    break;
            }
        }
    }

    private static bool IsNonShipping(string fqn)
        => NonShippingPrefixes.Any(p => fqn == p || fqn.StartsWith(p + ".", StringComparison.Ordinal));

    private static bool Resolves(string fqn, IReadOnlyDictionary<string, List<Type>> types, IReadOnlySet<string> namespaces)
    {
        if (types.ContainsKey(fqn) || namespaces.Contains(fqn))
            return true;

        // Attribute usages drop the "Attribute" suffix (e.g. [Plugin] -> PluginAttribute).
        if (types.ContainsKey(fqn + "Attribute"))
            return true;

        // Longest resolvable type prefix; the next segment must then be a real member of that type.
        var segments = fqn.Split('.');
        for (var take = segments.Length - 1; take >= 2; take--)
        {
            var typeName = string.Join('.', segments[..take]);
            if (types.TryGetValue(typeName, out var candidates) && candidates.Any(t => HasMember(t, segments[take])))
                return true;
        }

        return false;
    }

    private static bool HasMember(Type type, string member)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        if (type.GetMember(member, Flags).Length > 0)
            return true;

        // Interfaces do not surface members inherited from base interfaces via GetMember; walk them.
        return type.IsInterface && type.GetInterfaces().Any(i => i.GetMember(member, Flags).Length > 0);
    }

    private static (Dictionary<string, List<Type>> Types, HashSet<string> Namespaces) BuildShippingIndex()
    {
        var types = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in ArchTestSupport.ShippingAssemblies())
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (type.FullName is null)
                    continue;

                var key = NormalizeTypeName(type.FullName);
                if (!types.TryGetValue(key, out var list))
                    types[key] = list = [];
                list.Add(type);

                AddNamespaceChain(namespaces, type.Namespace);
            }
        }

        return (types, namespaces);
    }

    private static void AddNamespaceChain(HashSet<string> namespaces, string? ns)
    {
        if (string.IsNullOrEmpty(ns))
            return;

        var index = 0;
        while (index >= 0)
        {
            index = ns.IndexOf('.', index + 1);
            namespaces.Add(index < 0 ? ns : ns[..index]);
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static string NormalizeTypeName(string fullName)
    {
        // Strip generic arity (`1) and render nested types with '.' as they appear in source text.
        var tick = fullName.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
            fullName = fullName[..tick];
        return fullName.Replace('+', '.');
    }
}
