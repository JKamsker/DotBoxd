using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    /// <summary>
    /// True when <paramref name="type"/> implements <c>System.Collections.Generic.IEnumerable&lt;T&gt;</c>.
    /// A collection reaches <see cref="IsRecordDto"/> only after the recognized list/map shapes have already
    /// been ruled out, so any remaining enumerable — e.g. <c>ImmutableArray&lt;T&gt;</c>,
    /// <c>ImmutableList&lt;T&gt;</c>, <c>Queue&lt;T&gt;</c> — exposes only scalar getters (<c>Length</c>,
    /// <c>Count</c>, …) and would otherwise be mis-marshalled as a metadata-only record that silently drops its
    /// elements. Excluding it makes the type fail closed with a clear "not supported" diagnostic instead.
    /// Recognized lists/maps never reach here, and a plain DTO does not implement <c>IEnumerable&lt;T&gt;</c>,
    /// so this does not over-exclude.
    /// </summary>
    private static bool ImplementsGenericEnumerable(ITypeSymbol type)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return true;
            }
        }

        return false;
    }
}
