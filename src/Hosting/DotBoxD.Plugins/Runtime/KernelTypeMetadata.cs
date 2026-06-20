namespace DotBoxD.Plugins.Runtime;

internal static class KernelTypeMetadata
{
    public static string PluginId(Type kernelType)
    {
        var attribute = Attribute.GetCustomAttribute(kernelType, typeof(PluginAttribute)) as PluginAttribute;
        if (attribute is null || string.IsNullOrWhiteSpace(attribute.Id))
        {
            throw new InvalidOperationException($"Kernel type '{kernelType.FullName}' must declare PluginAttribute.");
        }

        return attribute.Id;
    }
}
