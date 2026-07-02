using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    /// <summary>
    /// True when <paramref name="member"/> can be written through an object initializer: a property with an
    /// accessible <c>set</c>/<c>init</c>, or a non-readonly public field.
    /// </summary>
    public static bool IsObjectInitializerWritable(RecordMember member, Compilation? compilation = null)
        => member.Symbol switch
        {
            IPropertySymbol { SetMethod: { } setter } => IsAccessibleFromGeneratedCode(setter, compilation),
            IFieldSymbol { IsReadOnly: false, IsConst: false } field => IsAccessibleFromGeneratedCode(field, compilation),
            _ => false
        };

    public static bool IsReadableFromGeneratedCode(RecordMember member, Compilation? compilation = null)
        => member.Symbol switch
        {
            IPropertySymbol { GetMethod: { } getter } => IsAccessibleFromGeneratedCode(getter, compilation),
            IFieldSymbol field => IsAccessibleFromGeneratedCode(field, compilation),
            _ => false
        };

    public static bool IsAccessibleFromGeneratedCode(ISymbol symbol, Compilation? compilation)
        => compilation is null
            ? symbol.DeclaredAccessibility == Accessibility.Public
            : compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);
}
