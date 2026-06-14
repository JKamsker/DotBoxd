namespace SafeIR;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

// Disambiguate from SafeIR.Expression (the IR model record) which otherwise wins
// name resolution inside the SafeIR namespace and shadows the expression-tree type.
using LinqExpression = System.Linq.Expressions.Expression;

/// <summary>
/// Reads custom capability grant parameters into an immutable string dictionary.
///
/// For object parameter shapes the readable property set (public, instance, non-indexer,
/// public getter) and an invariant string accessor are discovered once per runtime
/// <see cref="Type"/> and cached, so building many policies that reuse the same anonymous or
/// options type pays the reflection metadata enumeration and accessor compilation a single
/// time instead of once per grant (PAL-0029). The per-grant dictionary snapshot is preserved
/// so each grant still owns an independent immutable parameter map.
/// </summary>
internal static class ParameterReader
{
    // Keyed by the concrete parameter runtime type. Each entry is the ordered set of readable
    // accessors in the same order GetProperties returned them, so the produced dictionary key
    // order and duplicate-name behavior match the original per-grant reflection path exactly.
    private static readonly ConcurrentDictionary<Type, PropertyAccessor[]> AccessorsByType = new();

    public static IReadOnlyDictionary<string, string> Read(object parameters)
    {
        if (parameters is IReadOnlyDictionary<string, string> values)
        {
            return new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(values, StringComparer.Ordinal));
        }

        var accessors = AccessorsByType.GetOrAdd(parameters.GetType(), BuildAccessors);
        var dictionary = new Dictionary<string, string>(accessors.Length, StringComparer.Ordinal);
        for (var i = 0; i < accessors.Length; i++)
        {
            var accessor = accessors[i];
            // Add (not indexer) preserves the original behavior of throwing on duplicate
            // property names surfaced by GetProperties (e.g. new-hidden members).
            dictionary.Add(accessor.Name, accessor.Read(parameters) ?? "");
        }

        return new ReadOnlyDictionary<string, string>(dictionary);
    }

    private static PropertyAccessor[] BuildAccessors(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var accessors = new List<PropertyAccessor>(properties.Length);
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (property.GetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            accessors.Add(new PropertyAccessor(property.Name, CompileReader(property)));
        }

        return accessors.ToArray();
    }

    // Compiles instance => Convert.ToString((object?)((TDeclaring)instance).Property, InvariantCulture)
    // so the cached path avoids a reflection GetValue invoke while preserving the exact invariant
    // string conversion the original ParameterReader produced.
    private static Func<object, string?> CompileReader(PropertyInfo property)
    {
        var instance = LinqExpression.Parameter(typeof(object), "instance");
        var typedInstance = LinqExpression.Convert(instance, property.DeclaringType!);
        var propertyAccess = LinqExpression.Property(typedInstance, property);
        var boxedValue = LinqExpression.Convert(propertyAccess, typeof(object));

        var convertToString = typeof(Convert).GetMethod(
            nameof(Convert.ToString),
            [typeof(object), typeof(IFormatProvider)])!;
        var invariant = LinqExpression.Constant(CultureInfo.InvariantCulture, typeof(IFormatProvider));
        var body = LinqExpression.Call(convertToString, boxedValue, invariant);

        return LinqExpression.Lambda<Func<object, string?>>(body, instance).Compile();
    }

    private readonly record struct PropertyAccessor(string Name, Func<object, string?> Read);
}
