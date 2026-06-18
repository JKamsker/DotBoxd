using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core;

public sealed class SandboxTypeTests
{
    [Theory]
    [InlineData("Object")]
    [InlineData("System.String")]
    [InlineData("Microsoft.Extensions.Logging.ILogger")]
    public void IsKnown_rejects_forbidden_scalar_names(string name)
    {
        Assert.False(SandboxType.Scalar(name).IsKnown());
    }

    [Fact]
    public void IsKnown_rejects_forbidden_names_nested_in_structural_types()
    {
        var types = new[] {
            SandboxType.List(SandboxType.Scalar("System.String")),
            SandboxType.Map(SandboxType.String, SandboxType.Scalar("System.String")),
            SandboxType.Record([SandboxType.I32, SandboxType.Scalar("System.String")])
        };

        foreach (var type in types)
        {
            Assert.False(type.IsKnown());
        }
    }

    [Fact]
    public void IsKnown_with_declared_opaque_ids_rejects_forbidden_nested_types()
    {
        var declaredOpaqueIds = new HashSet<string>(StringComparer.Ordinal) { "PlayerId" };
        var allowed = SandboxType.Map(
            SandboxType.Scalar("PlayerId"),
            SandboxType.List(SandboxType.Scalar("PlayerId")));
        var forbidden = SandboxType.Record([
            SandboxType.Scalar("PlayerId"),
            SandboxType.Scalar("System.String")
        ]);

        Assert.True(allowed.IsKnown(declaredOpaqueIds));
        Assert.False(forbidden.IsKnown(declaredOpaqueIds));
    }

    [Fact]
    public void IsKnownBuiltIn_rejects_forbidden_names_nested_in_structural_types()
    {
        var type = SandboxType.Record([
            SandboxType.String,
            SandboxType.Scalar("System.String")
        ]);

        Assert.False(type.IsKnownBuiltIn());
    }
}
