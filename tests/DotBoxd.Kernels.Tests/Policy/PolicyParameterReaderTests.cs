namespace DotBoxd.Kernels.Tests;

public sealed class PolicyParameterReaderTests
{
    [Fact]
    public void Grant_object_parameters_include_only_public_instance_non_indexer_getters()
    {
        var policy = SandboxPolicyBuilder.Create()
            .Grant("test.capability", new GrantParameterShape())
            .Build();

        var grant = Assert.Single(policy.Grants);
        var parameter = Assert.Single(grant.Parameters);
        Assert.Equal("PublicValue", parameter.Key);
        Assert.Equal("visible", parameter.Value);
    }

    private sealed class GrantParameterShape
    {
        public static string StaticValue => "static";

        public string PublicValue => "visible";

        public string PrivateGetter { private get; set; } = "hidden";

        public string this[int index] => "indexed";
    }
}
