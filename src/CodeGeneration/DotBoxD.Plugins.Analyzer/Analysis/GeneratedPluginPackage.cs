namespace DotBoxD.Plugins.Analyzer.Analysis;

internal sealed record GeneratedPluginPackage(
    string HintName,
    string Source,
    string Namespace,
    string PackageName);

internal readonly record struct GeneratedPluginPackageIdentity(
    string Namespace,
    string PackageName)
{
    public static GeneratedPluginPackageIdentity From(GeneratedPluginPackage package)
        => new(package.Namespace, package.PackageName);

    public string NamespaceDisplay
        => string.IsNullOrWhiteSpace(Namespace) ? "<global>" : Namespace;
}

internal sealed record GeneratedPluginPackageDiagnostic(string Message);
