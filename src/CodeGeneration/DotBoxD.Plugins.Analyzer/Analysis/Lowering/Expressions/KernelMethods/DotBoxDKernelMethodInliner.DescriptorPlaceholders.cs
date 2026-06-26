using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDKernelMethodInliner
{
    private static IReadOnlyList<DescriptorPlaceholderOccurrence> ValidateDescriptorParameters(
        IMethodSymbol method,
        KernelMethodDescriptorPayload descriptor)
    {
        var expression = SyntaxFactory.ParseExpression(descriptor.Source);
        if (expression.ContainsDiagnostics)
        {
            throw new NotSupportedException("Generated descriptor contains invalid expression source.");
        }

        var identifierSpans = new HashSet<TextSpan>(expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Select(static identifier => identifier.Identifier.Span));

        var occurrences = new List<DescriptorPlaceholderOccurrence>();
        if (descriptor.Parameters.Count != method.Parameters.Length)
        {
            throw new NotSupportedException(
                $"Generated descriptor for context [KernelMethod] '{method.Name}' has stale parameter metadata.");
        }

        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var expectedType = DotBoxDTypeNameReader.KernelMethodTypeName(method.Parameters[i].Type);
            var expectedPlaceholder = DescriptorPlaceholder(i);
            var actual = descriptor.Parameters[i];
            if (!string.Equals(actual.Placeholder, expectedPlaceholder, StringComparison.Ordinal) ||
                !string.Equals(actual.Type, expectedType, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Generated descriptor for context [KernelMethod] '{method.Name}' has stale parameter metadata.");
            }

            foreach (var span in ValidatePlaceholderOccurrences(method, descriptor.Source, expectedPlaceholder, identifierSpans))
            {
                occurrences.Add(new DescriptorPlaceholderOccurrence(i, span));
            }
        }

        return occurrences;
    }

    private static IReadOnlyList<TextSpan> ValidatePlaceholderOccurrences(
        IMethodSymbol method,
        string source,
        string placeholder,
        HashSet<TextSpan> identifierSpans)
    {
        var occurrences = new List<TextSpan>();
        var start = 0;
        while (start < source.Length)
        {
            var index = source.IndexOf(placeholder, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return occurrences;
            }

            var span = new TextSpan(index, placeholder.Length);
            if (!identifierSpans.Contains(span))
            {
                throw new NotSupportedException(
                    $"Generated descriptor for context [KernelMethod] '{method.Name}' has stale parameter metadata.");
            }

            occurrences.Add(span);
            start = index + placeholder.Length;
        }

        return occurrences;
    }

    private static string ReplaceDescriptorPlaceholders(
        string source,
        IReadOnlyList<DescriptorPlaceholderReplacement> replacements)
    {
        var builder = new StringBuilder(source);
        foreach (var replacement in replacements.OrderByDescending(static replacement => replacement.Span.Start))
        {
            builder.Remove(replacement.Span.Start, replacement.Span.Length);
            builder.Insert(replacement.Span.Start, replacement.Source);
        }

        return builder.ToString();
    }

    private static void ValidateDescriptorArgumentUses(
        IMethodSymbol method,
        IReadOnlyList<DescriptorPlaceholderOccurrence> occurrences,
        BoundKernelMethodCall call)
    {
        var usageCounts = new Dictionary<IParameterSymbol, int>(SymbolEqualityComparer.Default);
        var firstUseOrder = new List<IParameterSymbol>();
        foreach (var occurrence in occurrences.OrderBy(static occurrence => occurrence.Span.Start))
        {
            var parameter = method.Parameters[occurrence.ParameterIndex];
            usageCounts[parameter] = usageCounts.TryGetValue(parameter, out var count) ? count + 1 : 1;
            if (!firstUseOrder.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, parameter)))
            {
                firstUseOrder.Add(parameter);
            }
        }

        KernelMethodArgumentReuseValidator.Validate(method, usageCounts, firstUseOrder, call);
    }

    private static string DescriptorPlaceholder(int index)
        => "__dotboxd_kernel_method_arg_" +
           index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
           "__";

    private sealed record DescriptorPlaceholderOccurrence(int ParameterIndex, TextSpan Span);

    private sealed record DescriptorPlaceholderReplacement(TextSpan Span, string Source);
}
