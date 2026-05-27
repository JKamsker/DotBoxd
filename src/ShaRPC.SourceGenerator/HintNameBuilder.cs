namespace ShaRPC.SourceGenerator;

internal static class HintNameBuilder
{
    /// <summary>
    /// Builds the hint-name prefix for the per-service generated files. Includes the
    /// namespace so two services that share a simple interface name don't collide on
    /// SourceProductionContext.AddSource.
    /// </summary>
    public static string Prefix(ServiceModel model)
    {
        if (string.IsNullOrEmpty(model.Namespace))
        {
            return model.InterfaceName;
        }
        return NamespaceIdentifierPrefix(model.Namespace) + "_" + model.InterfaceName;
    }

    public static string NamespaceIdentifierPrefix(string namespaceName)
    {
        var flattened = namespaceName.Replace('.', '_');
        if (namespaceName.IndexOf('_') < 0)
        {
            return flattened;
        }

        return flattened + "__" + StableHash(namespaceName);
    }

    private static string StableHash(string value)
    {
        unchecked
        {
            ulong hash = 14695981039346656037;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 1099511628211;
            }

            return hash.ToString("x16");
        }
    }
}
