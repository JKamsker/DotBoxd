using System.Reflection;
using SafeIR.Hosting;
using SafeIR.Serialization.Json;

namespace SafeIR.Tests;

public sealed class JsonApiSurfaceTests
{
    [Fact]
    public void Public_surface_uses_import_not_parse_or_script_terms()
    {
        var jsonExtensionMethods = typeof(SandboxHostJsonExtensions)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains(jsonExtensionMethods, name => name == "ImportJsonAsync");
        Assert.DoesNotContain(jsonExtensionMethods, name => name.Contains("Parse", StringComparison.Ordinal));
        Assert.DoesNotContain(jsonExtensionMethods, name => name.Contains("Script", StringComparison.Ordinal));
        Assert.DoesNotContain(Enum.GetNames<SandboxErrorCode>(), name => name.Contains("Parse", StringComparison.Ordinal));
    }
}
