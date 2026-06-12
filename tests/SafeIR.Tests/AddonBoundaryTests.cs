using System.Xml.Linq;

namespace SafeIR.Tests;

public sealed class AddonBoundaryTests
{
    private static readonly string[] CoreLibraryDirectories = [
        "src/SafeIR.Core",
        "src/SafeIR.Validation",
        "src/SafeIR.Hosting",
        "src/SafeIR.Runtime"
    ];

    private static readonly string[] CoreLibraryProjects = [
        "src/SafeIR.Core/SafeIR.Core.csproj",
        "src/SafeIR.Validation/SafeIR.Validation.csproj",
        "src/SafeIR.Hosting/SafeIR.Hosting.csproj",
        "src/SafeIR.Runtime/SafeIR.Runtime.csproj"
    ];

    private static readonly string[] ForbiddenCoreTokens = [
        "System.Text.Json",
        "SafeIrJsonImporter",
        "JsonDocument",
        "JsonElement",
        "SafeHttp",
        "net.http.get",
        "MessagePack",
        "ShaRPC"
    ];

    private static readonly string[] ForbiddenAddonReferences = [
        "SafeIR.Serialization.Json",
        "SafeIR.Transport.Http",
        "SafeIR.Transport.Ipc",
        "SafeIR.Plugins",
        "SafeIR.PluginAnalyzer",
        "System.Text.Json",
        "MessagePack",
        "ShaRPC"
    ];

    [Theory]
    [MemberData(nameof(CoreLibraryForbiddenTokens))]
    public async Task Core_libraries_do_not_reference_serialization_or_transport_addons(
        string relativeDirectory,
        string forbiddenToken)
    {
        var directory = Path.Combine(RepositoryRoot(), relativeDirectory);
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories);
        foreach (var file in files) {
            var text = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain(forbiddenToken, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(CoreLibraryProjectPaths))]
    public void Core_library_projects_have_no_addon_dependencies(string relativeProject)
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), relativeProject));
        var references = project
            .Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? "")
            .ToArray();

        foreach (var forbidden in ForbiddenAddonReferences) {
            Assert.DoesNotContain(references, reference => reference.Contains(forbidden, StringComparison.Ordinal));
        }
    }

    public static TheoryData<string, string> CoreLibraryForbiddenTokens()
    {
        var data = new TheoryData<string, string>();
        foreach (var directory in CoreLibraryDirectories) {
            foreach (var token in ForbiddenCoreTokens) {
                data.Add(directory, token);
            }
        }

        return data;
    }

    public static TheoryData<string> CoreLibraryProjectPaths()
    {
        var data = new TheoryData<string>();
        foreach (var project in CoreLibraryProjects) {
            data.Add(project);
        }

        return data;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SafeIR.slnx"))) {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
