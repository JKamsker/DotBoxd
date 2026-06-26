using System.Collections;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static IEnumerable<(object? Key, object? Value)> MapEntries(
        IEnumerable enumerable,
        Type keyType,
        Type valueType)
    {
        if (enumerable is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                yield return (entry.Key, entry.Value);
            }

            yield break;
        }

        foreach (var entry in enumerable)
        {
            if (entry is null)
            {
                throw new ArgumentException("Kernel RPC service dictionary entry was null.");
            }

            var entryType = entry.GetType();
            if (!IsKeyValuePair(entryType, keyType, valueType))
            {
                throw new ArgumentException(
                    $"Kernel RPC service expected dictionary entries of '{typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType)}'.");
            }

            yield return (
                entryType.GetProperty(nameof(KeyValuePair<object, object>.Key))!.GetValue(entry),
                entryType.GetProperty(nameof(KeyValuePair<object, object>.Value))!.GetValue(entry));
        }
    }

    private static bool IsKeyValuePair(Type type, Type keyType, Type valueType)
        => type.IsGenericType &&
           type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>) &&
           type.GetGenericArguments() is [var actualKey, var actualValue] &&
           actualKey == keyType &&
           actualValue == valueType;
}
