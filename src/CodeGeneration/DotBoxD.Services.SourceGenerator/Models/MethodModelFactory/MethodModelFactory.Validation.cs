using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static partial class MethodModelFactory
{
    private static string ParameterRefKindKeyword(RefKind kind) =>
        kind.ToString() switch
        {
            "Ref" => "ref ",
            "In" => "in ",
            "Out" => "out ",
            "RefReadOnly" => "ref readonly ",
            "RefReadOnlyParameter" => "ref readonly ",
            _ => string.Empty,
        };

    private static string ParameterScopeKeyword(IParameterSymbol parameter, CancellationToken ct)
    {
        foreach (var syntaxRef in parameter.DeclaringSyntaxReferences)
        {
            ct.ThrowIfCancellationRequested();
            if (syntaxRef.GetSyntax(ct) is not ParameterSyntax syntax)
            {
                continue;
            }

            foreach (var modifier in syntax.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.ScopedKeyword))
                {
                    return "scoped ";
                }
            }
        }

        return string.Empty;
    }

    private static string ReturnRefKindKeyword(RefKind kind) =>
        kind.ToString() switch
        {
            "Ref" => "ref ",
            "In" => "ref readonly ",
            "RefReadOnly" => "ref readonly ",
            "RefReadOnlyParameter" => "ref readonly ",
            _ => string.Empty,
        };

    private static string RefKindDisplay(RefKind kind, bool isReturn)
    {
        var text = kind.ToString();
        return text switch
        {
            "In" when isReturn => "ref readonly",
            "RefReadOnly" => "ref readonly",
            "RefReadOnlyParameter" => "ref readonly",
            _ => text.ToLowerInvariant(),
        };
    }

    private static void SetUnsupported(
        ref string? unsupportedReason,
        ref DiagnosticLocation unsupportedLocation,
        string? reason,
        DiagnosticLocation location)
    {
        if (unsupportedReason is not null || reason is null)
        {
            return;
        }

        unsupportedReason = reason;
        unsupportedLocation = location;
    }

    private static (ParameterStreamKind Kind, ITypeSymbol? ItemType) ClassifyParameterStream(
        ITypeSymbol type,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ReturnTypeClassifier.TryGetAsyncEnumerableItemType(type, out var itemType))
        {
            return (ParameterStreamKind.AsyncEnumerable, itemType);
        }

        if (ReturnTypeClassifier.IsStream(type))
        {
            return (ParameterStreamKind.Stream, null);
        }

        if (ReturnTypeClassifier.IsPipe(type))
        {
            return (ParameterStreamKind.Pipe, null);
        }

        return (ParameterStreamKind.None, null);
    }

    private static string? GetUnsupportedParameterTypeReason(
        ITypeSymbol type,
        ParameterStreamKind streamKind,
        ITypeSymbol? streamItemType,
        string parameterName,
        CancellationToken ct)
    {
        var target = streamKind == ParameterStreamKind.AsyncEnumerable && streamItemType is not null
            ? streamItemType
            : type;
        return RpcTypeValidator.GetUnsupportedTypeReason(target, $"parameter '{parameterName}'", ct);
    }

    private static string? GetUnsupportedNullableStreamingReturnReason(
        ITypeSymbol returnType,
        MethodReturnKind returnKind)
    {
        if (returnKind == MethodReturnKind.Stream ||
            returnKind == MethodReturnKind.Pipe ||
            returnKind == MethodReturnKind.AsyncEnumerable)
        {
            return returnType.NullableAnnotation == NullableAnnotation.Annotated
                ? "nullable streaming return values are not supported; streams cannot be null"
                : null;
        }

        if (NamingHelpers.IsStreamReturn(returnKind) ||
            NamingHelpers.IsPipeReturn(returnKind) ||
            NamingHelpers.IsAsyncEnumerableReturn(returnKind))
        {
            if (returnType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named &&
                named.TypeArguments[0].NullableAnnotation == NullableAnnotation.Annotated)
            {
                return "nullable streaming return values are not supported; streams cannot be null";
            }
        }

        return null;
    }

    private static string? GetUnsupportedNullableStreamingParameterReason(
        ITypeSymbol type,
        ParameterStreamKind streamKind,
        string parameterName)
    {
        if (streamKind == ParameterStreamKind.None ||
            type.NullableAnnotation != NullableAnnotation.Annotated)
        {
            return null;
        }

        return $"nullable streamed parameter '{parameterName}' is not supported; streams cannot be null";
    }

    private static string? GetUnsupportedParameterSubServiceReason(
        ITypeSymbol type,
        ParameterStreamKind streamKind,
        ITypeSymbol? streamItemType,
        string parameterName,
        CancellationToken ct,
        RpcTypeValidationCache validationCache)
    {
        var target = streamKind == ParameterStreamKind.AsyncEnumerable && streamItemType is not null
            ? streamItemType
            : type;
        return RpcTypeValidator.GetUnsupportedSubServicePayloadReason(
            target,
            $"parameter '{parameterName}'",
            ct,
            validationCache);
    }
}
