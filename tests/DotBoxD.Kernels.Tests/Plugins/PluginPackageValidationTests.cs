using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_manifest_plugin_id_that_does_not_match_module()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { PluginId = "other-plugin" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK011");
    }

    [Fact]
    public async Task Install_rejects_manifest_effects_that_do_not_match_verified_module()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Effects = ["Cpu", "HostStateWrite", "Audit"] } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK041");
    }

    [Fact]
    public async Task Install_rejects_manifest_required_capabilities_that_do_not_match_verified_module()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        Assert.NotEmpty(package.Manifest.RequiredCapabilities);
        var invalid = package with { Manifest = package.Manifest with { RequiredCapabilities = [] } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK044");
    }

    [Fact]
    public async Task Install_rejects_manifest_required_capabilities_that_self_assert_unverified_capability()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                RequiredCapabilities = [.. package.Manifest.RequiredCapabilities, "file.write"]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK044");
    }

    [Fact]
    public async Task Install_rejects_indexed_predicate_value_that_does_not_match_its_declared_type()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var subscription = package.Manifest.Subscriptions[0];
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    subscription with
                    {
                        // valueType says int but the boxed value is a long — only reachable by building a
                        // manifest in-memory (the JSON importer parses per valueType).
                        IndexedPredicates =
                            [new IndexedPredicate("Damage", IndexPredicateOperator.Equals, 5L, "int")],
                    }
                ]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK049");
    }

    [Fact]
    public async Task Install_rejects_full_index_coverage_with_no_indexed_predicates()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var subscription = package.Manifest.Subscriptions[0];
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [subscription with { IndexCoversPredicate = true }]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK048");
    }

    [Fact]
    public async Task Install_rejects_missing_kernel_entrypoint()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Entrypoints = package.Entrypoints with { Handle = "MissingHandle" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK032");
    }

    [Fact]
    public async Task Install_rejects_subscription_kernel_that_does_not_match_module_metadata()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [new HookSubscriptionManifest("DamageEvent", "OtherKernel")]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK013");
    }

    [Fact]
    public async Task Manifest_compiled_mode_does_not_force_plugin_compiler_dispatch()
    {
        var compiler = new FailingCompiler();
        var messages = new InMemoryPluginMessageSink();
        var server = DotBoxD.Plugins.PluginServer.Create(
            messages,
            builder => builder.UseCompilerIfAvailable(compiler),
            PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var compiledManifest = package.Manifest with { Mode = ExecutionMode.Compiled };
        await server.InstallAsync(package with { Manifest = compiledManifest });

        server.Hooks.On<DamageEvent>().Use<FireDamageKernel>();
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Single(messages.Messages);
        Assert.Equal(0, compiler.Calls);
    }

    [Fact]
    public async Task Install_rejects_unsupported_manifest_execution_mode()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Mode = (ExecutionMode)123 } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK042");
    }

    [Fact]
    public async Task Install_rejects_manifest_contract_that_does_not_match_subscription_event()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Contract = "IItemFilter" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK014" &&
            d.Message.Contains("IEventKernel<TEvent>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_rejects_event_kernel_contract_with_different_event_than_subscription()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Contract = "IEventKernel<OtherEvent>" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK014" &&
            d.Message.Contains("must match subscription event", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_rejects_wrong_should_handle_return_type()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var shouldHandle = package.Module.Functions.Single(f => f.Id == package.Entrypoints.ShouldHandle);
        var span = new SourceSpan(1, 1);
        var functions = package.Module.Functions
            .Select(f => f.Id == shouldHandle.Id
                ? f with
                {
                    ReturnType = SandboxType.I32,
                    Body = [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(1), span), span)]
                }
                : f)
            .ToArray();
        var invalid = package with { Module = package.Module with { Functions = functions } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK033");
    }

    [Fact]
    public async Task Install_rejects_live_setting_missing_from_entrypoint_parameters()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var handle = package.Module.Functions.Single(f => f.Id == package.Entrypoints.Handle);
        var functions = package.Module.Functions
            .Select(f => f.Id == handle.Id
                ? f with { Parameters = f.Parameters.Where(p => p.Name != "MinDamage").ToArray() }
                : f)
            .ToArray();
        var invalid = package with { Module = package.Module with { Functions = functions } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK034" || d.Code == "DBXK035");
    }

    [Fact]
    public async Task Install_rejects_event_and_live_setting_parameters_in_wrong_order()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var package = FireDamagePluginPackage.Create();
        var functions = package.Module.Functions
            .Select(f => f.Id == package.Entrypoints.ShouldHandle || f.Id == package.Entrypoints.Handle
                ? f with { Parameters = f.Parameters.OrderByDescending(p => p.Name == "MinDamage").ToArray() }
                : f)
            .ToArray();
        var invalid = package with { Module = package.Module with { Functions = functions } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK033");
    }

    private sealed class FailingCompiler : ISandboxCompiler
    {
        public int Calls { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("plugin manifest must not force compiled execution");
        }
    }
}
