using SafeIR.Runtime;

namespace SafeIR.Tests;

public sealed class HashIdentityTests
{
    [Fact]
    public void Policy_hash_includes_grant_expiration()
    {
        var first = PolicyWithGrant(FileReadGrant(DateTimeOffset.UnixEpoch.AddHours(1)));
        var second = PolicyWithGrant(FileReadGrant(DateTimeOffset.UnixEpoch.AddHours(2)));

        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Policy_hash_distinguishes_delimiter_heavy_grant_parameters()
    {
        var first = PolicyWithGrant(new CapabilityGrant(
            "test.capability",
            new Dictionary<string, string>
            {
                ["a=b"] = "c"
            }));
        var second = PolicyWithGrant(new CapabilityGrant(
            "test.capability",
            new Dictionary<string, string>
            {
                ["a"] = "b=c"
            }));

        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Binding_registry_freezes_parameter_lists_before_hashing_manifest()
    {
        var parameters = new List<SandboxType> { SandboxType.I32 };
        var registry = new BindingRegistryBuilder()
            .Add(TestBinding("test.mutable", parameters))
            .Build();
        var hash = registry.ManifestHash;

        parameters[0] = SandboxType.I64;

        var signature = Assert.Single(registry.Signatures);
        Assert.Equal(hash, registry.ManifestHash);
        Assert.Equal(SandboxType.I32, Assert.Single(signature.Parameters));
    }

    [Fact]
    public void Binding_manifest_hash_distinguishes_delimiter_heavy_signatures()
    {
        var first = new BindingRegistryBuilder()
            .Add(TestBinding("a@b", [SandboxType.I32]))
            .Build();
        var second = new BindingRegistryBuilder()
            .Add(TestBinding("a", [SandboxType.I32]))
            .Build();

        Assert.NotEqual(first.ManifestHash, second.ManifestHash);
    }

    [Fact]
    public void Policy_hash_includes_loop_iteration_limit()
    {
        var first = SandboxPolicyBuilder.Create().WithMaxLoopIterations(10).Build();
        var second = SandboxPolicyBuilder.Create().WithMaxLoopIterations(11).Build();

        Assert.NotEqual(first.Hash, second.Hash);
    }

    private static SandboxPolicy PolicyWithGrant(CapabilityGrant grant)
        => new(
            "hash-test",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [grant],
            new ResourceLimits(MaxFuel: 1_000));

    private static CapabilityGrant FileReadGrant(DateTimeOffset expiresAt)
        => new(
            "file.read",
            new Dictionary<string, string>
            {
                ["root"] = "root",
                ["maxBytesPerRun"] = "1024"
            },
            expiresAt);

    private static BindingDescriptor TestBinding(string id, IReadOnlyList<SandboxType> parameters)
        => new(
            id,
            SemVersion.One,
            parameters,
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
