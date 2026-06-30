using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Map (<c>Dictionary&lt;K,V&gt;</c>) body lowering for <see cref="DotBoxDRpcJsonLowerer"/>: builds and reads
/// maps through the kernel's immutable <c>map.*</c> intrinsics. <c>new Dictionary&lt;K,V&gt;()</c> or
/// <c>new()</c> →
/// <c>map.empty</c>, <c>dict[key]</c> → <c>map.get</c>, <c>dict[key] = value</c> → a rebinding <c>map.set</c>,
/// and <c>dict.ContainsKey(key)</c> → <c>map.containsKey</c>.
/// </summary>
internal sealed partial class DotBoxDRpcJsonLowerer
{
    // new Dictionary<K,V>() or new() → an empty typed map. Comparer-bearing constructors fail closed because
    // kernel maps have fixed wire equality semantics that cannot preserve caller-provided comparers.
    private string? TryLowerMapCreation(BaseObjectCreationExpressionSyntax creation, ITypeSymbol created)
    {
        if (DotBoxDRpcTypeMapper.MapTypes(created) is null)
        {
            return null;
        }

        if (creation.Initializer is not null ||
            creation.ArgumentList is { Arguments.Count: > 0 })
        {
            throw new NotSupportedException(
                "Server extension map creation supports only an empty dictionary with default comparer semantics; comparer constructors, capacity constructors, and initializers are not supported.");
        }

        Allocates = true;
        return Call("map.empty", DotBoxDRpcTypeMapper.JsonType(created, _model.Compilation));
    }

    // dict[key] → map.get
    private string? TryLowerMapElementGet(ElementAccessExpressionSyntax element, ITypeSymbol receiverType)
    {
        if (element.ArgumentList.Arguments.Count != 1 ||
            DotBoxDRpcTypeMapper.MapTypes(receiverType) is null)
        {
            return null;
        }

        return Call(
            "map.get",
            null,
            LowerExpression(element.Expression),
            LowerExpression(element.ArgumentList.Arguments[0].Expression));
    }

    /// <summary>
    /// Lowers a supported method call on a map-typed receiver. Only <c>ContainsKey</c> is supported as an
    /// expression (returns <c>Bool</c>); returns null when the receiver is not a map so non-map invocations
    /// fall through to the host-binding path.
    /// </summary>
    private string? TryLowerMapMethod(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var receiverType = _model.GetTypeInfo(member.Expression, _cancellationToken).Type;
        if (receiverType is null || DotBoxDRpcTypeMapper.MapTypes(receiverType) is null)
        {
            return null;
        }

        if (string.Equals(member.Name.Identifier.ValueText, "ContainsKey", StringComparison.Ordinal) &&
            invocation.ArgumentList.Arguments.Count == 1)
        {
            return Call(
                "map.containsKey",
                null,
                LowerExpression(member.Expression),
                LowerExpression(invocation.ArgumentList.Arguments[0].Expression));
        }

        throw new NotSupportedException(
            $"Server extension map operation '{invocation}' is not supported; supported map calls are ContainsKey.");
    }

    /// <summary>
    /// Lowers <c>dict[key] = value</c> on a map-typed local to a rebinding <c>map.set</c>: because the kernel
    /// map is immutable, the assignment becomes <c>dict = map.set(dict, key, value)</c> (the same
    /// rebind-the-local shape <c>list.add</c> uses). Returns null when the indexed target is not a map local.
    /// </summary>
    private string? TryLowerMapIndexSet(
        ElementAccessExpressionSyntax element,
        ExpressionSyntax valueExpression,
        List<string> output)
    {
        var receiverType = _model.GetTypeInfo(element.Expression, _cancellationToken).Type;
        if (element.Expression is not IdentifierNameSyntax mapLocal ||
            element.ArgumentList.Arguments.Count != 1 ||
            receiverType is null ||
            DotBoxDRpcTypeMapper.MapTypes(receiverType) is null)
        {
            return null;
        }

        var name = mapLocal.Identifier.ValueText;
        var keyJson = LowerExpressionWithPrelude(element.ArgumentList.Arguments[0].Expression, output);
        var valueJson = LowerExpressionWithPrelude(valueExpression, output);
        Allocates = true;
        return SetStatement(name, Call("map.set", null, Var(name), keyJson, valueJson));
    }
}
