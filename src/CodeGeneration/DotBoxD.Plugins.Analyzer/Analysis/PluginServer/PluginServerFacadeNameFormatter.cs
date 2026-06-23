using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerFacadeNameFormatter
{
    internal static string ServiceWrapperName(INamedTypeSymbol serviceType)
    {
        var name = StripInterfacePrefix(serviceType.Name);
        return name + "PluginService";
    }

    internal static string SetupInterfaceName(string className)
    {
        var name = className.EndsWith("Server", StringComparison.Ordinal)
            ? className.Substring(0, className.Length - "Server".Length)
            : className;
        return "I" + name + "Setup";
    }

    internal static string ContextName(string className)
        => FacadeRootName(className) + "Context";

    internal static string HookRegistryName(string className)
        => FacadeRootName(className) + "HookRegistry";

    internal static string SubscriptionRegistryName(string className)
        => FacadeRootName(className) + "SubscriptionRegistry";

    internal static string ServerInterfaceName(INamedTypeSymbol worldType)
    {
        var name = StripInterfacePrefix(worldType.Name);
        if (name.EndsWith("Access", StringComparison.Ordinal) && name.Length > "Access".Length)
        {
            name = name.Substring(0, name.Length - "Access".Length);
        }

        return "I" + name + "Server";
    }

    internal static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    internal static string AccessibilityText(Accessibility accessibility)
        => accessibility switch
        {
            Microsoft.CodeAnalysis.Accessibility.Public => "public",
            Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
            _ => "internal"
        };

    private static string StripInterfacePrefix(string name)
    {
        if (name.StartsWith("I", StringComparison.Ordinal) && name.Length > 1 && char.IsUpper(name[1]))
        {
            return name.Substring(1);
        }

        return name;
    }

    private static string FacadeRootName(string className)
        => className.EndsWith("Server", StringComparison.Ordinal)
            ? className.Substring(0, className.Length - "Server".Length)
            : className;
}
