using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generation;

public sealed class LoweringFailClosedInvariantTests
{
    private static readonly NullSwitchFallbackExemption[] AllowedNullSwitchFallbacks =
    [
        new(
            "Lowering/Expressions/DotBoxDAnonymousObjectCreationExpressionLowerer.cs",
            "InitializerName",
            "Anonymous member-name inference returns null so the caller can reject unnameable fields with context."),
        new(
            "Lowering/Expressions/Primitives/DotBoxDConstantExpressionLowerer.cs",
            "LowerDefault",
            "Default-literal lowering returns null for normal scalar defaults that are handled by the generic path."),
        new(
            "Lowering/Expressions/KernelMethods/DotBoxDKernelMethodInliner.DescriptorShape.Helpers.cs",
            "SandboxTypeExpressionShape",
            "Descriptor shape probing returns null for absent or non-shape metadata before the caller rejects stale descriptors."),
        new(
            "Lowering/Expressions/KernelMethods/DotBoxDKernelMethodInliner.DescriptorShape.Helpers.cs",
            "SandboxTypeInvocationShape",
            "Descriptor shape probing returns null when the invocation is not a recognized SandboxType factory."),
        new(
            "Lowering/Expressions/KernelMethods/DotBoxDKernelMethodInliner.DescriptorShape.Helpers.cs",
            "SandboxTypeMemberShape",
            "Descriptor shape probing returns null when the member is not a recognized SandboxType singleton."),
    ];

    [Fact]
    public void Lowering_diagnostic_catalog_entries_are_documented_DBXK_rules()
    {
        var entries = LoweringDiagnosticCatalog.Entries;
        Assert.NotEmpty(entries);

        var duplicateKeys = entries
            .GroupBy(entry => entry.Surface + "|" + entry.Descriptor.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        Assert.Empty(duplicateKeys);

        var releaseText = AnalyzerReleaseText();
        foreach (var entry in entries)
        {
            Assert.StartsWith("DBXK", entry.Descriptor.Id, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(entry.Surface));
            Assert.False(string.IsNullOrWhiteSpace(entry.FactoryTypeName));
            Assert.False(string.IsNullOrWhiteSpace(entry.FailureRoute));
            Assert.False(string.IsNullOrWhiteSpace(entry.UnsupportedShapeFamily));
            Assert.Contains(entry.Descriptor.Id, releaseText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NotSupported_lowering_factory_catches_are_cataloged_and_route_to_diagnostics()
    {
        var catalogedFactories = LoweringDiagnosticCatalog.Entries
            .Select(entry => entry.FactoryTypeName)
            .Where(factory => !string.Equals(factory, "GeneratorGuard", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var catches = NotSupportedFactoryCatches().ToArray();
        var uncataloged = catches
            .Select(item => item.FactoryTypeName)
            .Distinct(StringComparer.Ordinal)
            .Where(factory => !catalogedFactories.Contains(factory))
            .ToArray();

        Assert.Empty(uncataloged);
        foreach (var item in catches)
        {
            if (string.Equals(item.FactoryTypeName, "HookChainModelFactory", StringComparison.Ordinal))
            {
                Assert.Contains("NotLoweredDiagnostic", item.MethodText, StringComparison.Ordinal);
                continue;
            }

            Assert.True(
                item.CatchText.Contains("Fail(", StringComparison.Ordinal) ||
                item.CatchText.Contains("PluginKernelDiagnostic", StringComparison.Ordinal),
                $"{item.FactoryTypeName}.{item.MethodName} catches NotSupportedException without routing to a diagnostic.");
        }
    }

    [Fact]
    public void Lowering_switch_fallbacks_do_not_return_null_from_dispatch_methods()
    {
        var offenders = new List<string>();
        var seenExemptions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in LoweringSourceFiles())
        {
            var relativePath = AnalysisRelativePath(file);
            var text = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();
            foreach (var arm in root.DescendantNodes().OfType<SwitchExpressionArmSyntax>())
            {
                if (!IsNullLiteral(arm.Expression))
                {
                    continue;
                }

                var methodName = EnclosingMethodName(arm);
                if (methodName.StartsWith("Try", StringComparison.Ordinal) ||
                    IsAllowedNullSwitchFallback(relativePath, methodName, seenExemptions))
                {
                    continue;
                }

                offenders.Add(FormatLocation(file, tree, arm, methodName));
            }
        }

        var staleExemptions = AllowedNullSwitchFallbacks
            .Where(exemption => !seenExemptions.Contains(exemption.Key))
            .Select(exemption => exemption.Key)
            .ToArray();
        Assert.Empty(staleExemptions);

        var unjustifiedExemptions = AllowedNullSwitchFallbacks
            .Where(exemption => string.IsNullOrWhiteSpace(exemption.Reason))
            .Select(exemption => exemption.Key)
            .ToArray();
        Assert.Empty(unjustifiedExemptions);

        Assert.True(
            offenders.Count == 0,
            "Lowering switch defaults must throw or call an Unsupported helper; nullable fallthrough is only for Try* recognizers:\n" +
            string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<FactoryCatch> NotSupportedFactoryCatches()
    {
        foreach (var file in Directory.EnumerateFiles(AnalysisRoot(), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var root = CSharpSyntaxTree.ParseText(text).GetRoot();
            foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
            {
                if (!IsNotSupportedExceptionCatch(catchClause))
                {
                    continue;
                }

                var typeName = catchClause.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
                if (typeName is null || (!typeName.EndsWith("ModelFactory", StringComparison.Ordinal) &&
                                         !string.Equals(typeName, "HookChainModelFactory", StringComparison.Ordinal)))
                {
                    continue;
                }

                var method = catchClause.Ancestors().OfType<MethodDeclarationSyntax>().First();
                if (!IsGeneratorEntryFactory(method.Identifier.ValueText))
                {
                    continue;
                }

                yield return new FactoryCatch(typeName, method.Identifier.ValueText, method.ToString(), catchClause.ToString());
            }
        }
    }

    private static bool IsGeneratorEntryFactory(string methodName)
        => string.Equals(methodName, "Create", StringComparison.Ordinal) ||
           string.Equals(methodName, "CreateTarget", StringComparison.Ordinal) ||
           string.Equals(methodName, "CreateRoot", StringComparison.Ordinal);

    private static bool IsNotSupportedExceptionCatch(CatchClauseSyntax catchClause)
        => catchClause.Declaration?.Type is NameSyntax name && IsNotSupportedExceptionName(name);

    private static bool IsNotSupportedExceptionName(NameSyntax name)
        => name switch
        {
            IdentifierNameSyntax identifier => string.Equals(
                identifier.Identifier.ValueText,
                nameof(NotSupportedException),
                StringComparison.Ordinal),
            QualifiedNameSyntax qualified => IsNotSupportedExceptionName(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => IsNotSupportedExceptionName(aliasQualified.Name),
            _ => false,
        };

    private static bool IsAllowedNullSwitchFallback(
        string relativePath,
        string methodName,
        HashSet<string> seenExemptions)
    {
        foreach (var exemption in AllowedNullSwitchFallbacks)
        {
            if (string.Equals(exemption.RelativePath, relativePath, StringComparison.Ordinal) &&
                string.Equals(exemption.MethodName, methodName, StringComparison.Ordinal))
            {
                seenExemptions.Add(exemption.Key);
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> LoweringSourceFiles()
    {
        var root = AnalysisRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.StartsWith("Lowering/", StringComparison.Ordinal) ||
                relative.StartsWith("Rpc/Lowering/", StringComparison.Ordinal))
            {
                yield return file;
            }
        }
    }

    private static string AnalyzerReleaseText()
    {
        var root = Path.Combine(RepositoryRoot(), "src", "CodeGeneration", "DotBoxD.Plugins.Analyzer");
        return File.ReadAllText(Path.Combine(root, "AnalyzerReleases.Shipped.md")) +
               File.ReadAllText(Path.Combine(root, "AnalyzerReleases.Unshipped.md"));
    }

    private static string AnalysisRoot()
        => Path.Combine(RepositoryRoot(), "src", "CodeGeneration", "DotBoxD.Plugins.Analyzer", "Analysis");

    private static string AnalysisRelativePath(string file)
        => Path.GetRelativePath(AnalysisRoot(), file).Replace(Path.DirectorySeparatorChar, '/');

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static bool IsNullLiteral(ExpressionSyntax expression)
        => expression.IsKind(SyntaxKind.NullLiteralExpression);

    private static string EnclosingMethodName(SyntaxNode node)
        => node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "<unknown>";

    private static string FormatLocation(string file, SyntaxTree tree, SyntaxNode node, string methodName)
    {
        var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        var relative = Path.GetRelativePath(RepositoryRoot(), file).Replace(Path.DirectorySeparatorChar, '/');
        return $"{relative}:{line} in {methodName}";
    }

    private sealed record FactoryCatch(
        string FactoryTypeName,
        string MethodName,
        string MethodText,
        string CatchText);

    private sealed record NullSwitchFallbackExemption(
        string RelativePath,
        string MethodName,
        string Reason)
    {
        public string Key => RelativePath + "#" + MethodName;
    }
}
