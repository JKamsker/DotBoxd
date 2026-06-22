using System.Reflection;

namespace DotBoxD.Plugins.Runtime;

internal sealed record PolymorphicHandleRuntimeMetadata(
    Type HandleType,
    Type KeyType,
    PropertyInfo? KeyProperty,
    FieldInfo? KeyField);

internal static class PolymorphicHandleRuntimeMetadataReader
{
    public static bool TryResolve(Type type, out PolymorphicHandleRuntimeMetadata metadata)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            var attribute = current.GetCustomAttribute<PolymorphicHandleAttribute>(inherit: false);
            if (attribute is null)
            {
                continue;
            }

            metadata = KeyMember(current, attribute.KeyMember) ??
                throw InvalidHandleMetadata(current);
            return true;
        }

        metadata = null!;
        return false;
    }

    private static PolymorphicHandleRuntimeMetadata? KeyMember(Type handleType, string keyMember)
    {
        for (var current = handleType; current is not null && current != typeof(object); current = current.BaseType)
        {
            var property = current.GetProperty(
                keyMember,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (property is { GetMethod.IsPublic: true } &&
                property.GetIndexParameters().Length == 0 &&
                IsSupportedKey(property.PropertyType))
            {
                return new PolymorphicHandleRuntimeMetadata(handleType, property.PropertyType, property, null);
            }

            var field = current.GetField(
                keyMember,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field is not null && IsSupportedKey(field.FieldType))
            {
                return new PolymorphicHandleRuntimeMetadata(handleType, field.FieldType, null, field);
            }
        }

        return null;
    }

    private static bool IsSupportedKey(Type type)
        => type == typeof(int) ||
           type == typeof(long) ||
           type == typeof(Guid) ||
           type == typeof(string);

    private static NotSupportedException InvalidHandleMetadata(Type handleType)
        => new(
            $"Polymorphic handle '{handleType}' must declare a public readable non-indexer key member of type int, long, Guid, or string.");
}
