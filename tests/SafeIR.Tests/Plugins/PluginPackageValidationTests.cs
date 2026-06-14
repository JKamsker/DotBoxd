using SafeIR;
using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Compiler;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_manifest_plugin_id_that_does_not_match_module()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { PluginId = "other-plugin" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP011");
    }

    [Fact]
    public async Task Install_rejects_manifest_effects_that_do_not_match_verified_module()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Effects = ["Cpu", "HostStateWrite", "Audit"] } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP041");
    }

    [Fact]
    public async Task Install_rejects_missing_kernel_entrypoint()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Entrypoints = package.Entrypoints with { Handle = "MissingHandle" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP032");
    }

    [Fact]
    public async Task Install_rejects_subscription_kernel_that_does_not_match_module_metadata()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [new HookSubscriptionManifest("DamageEvent", "OtherKernel")]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP013");
    }

    [Fact]
    public async Task Manifest_compiled_mode_does_not_force_plugin_compiler_dispatch()
    {
        var compiler = new FailingCompiler();
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(
            messages,
            builder => builder.UseCompilerIfAvailable(compiler),
            PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var compiledManifest = package.Manifest with { Mode = ExecutionMode.Compiled };
        await server.InstallAsync(package with { Manifest = compiledManifest });

        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Single(messages.Messages);
        Assert.Equal(0, compiler.Calls);
    }

    [Fact]
    public async Task Install_rejects_unsupported_manifest_execution_mode()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Mode = (ExecutionMode)123 } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP042");
    }

    [Fact]
    public async Task Install_rejects_manifest_contract_that_does_not_match_subscription_event()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Contract = "IItemFilter" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "SGP014" &&
            d.Message.Contains("IEventKernel<TEvent>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_rejects_event_kernel_contract_with_different_event_than_subscription()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Contract = "IEventKernel<OtherEvent>" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "SGP014" &&
            d.Message.Contains("must match subscription event", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_rejects_wrong_should_handle_return_type()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
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

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP033");
    }

    [Fact]
    public async Task Install_rejects_live_setting_missing_from_entrypoint_parameters()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var handle = package.Module.Functions.Single(f => f.Id == package.Entrypoints.Handle);
        var functions = package.Module.Functions
            .Select(f => f.Id == handle.Id
                ? f with { Parameters = f.Parameters.Where(p => p.Name != "MinDamage").ToArray() }
                : f)
            .ToArray();
        var invalid = package with { Module = package.Module with { Functions = functions } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP034" || d.Code == "SGP035");
    }

    [Fact]
    public async Task Install_rejects_event_and_live_setting_parameters_in_wrong_order()
    {
        var server = PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        server.RegisterEventAdapter(DamageEventAdapter.Instance);
        var package = FireDamagePluginPackage.Create();
        var functions = package.Module.Functions
            .Select(f => f.Id == package.Entrypoints.ShouldHandle || f.Id == package.Entrypoints.Handle
                ? f with { Parameters = f.Parameters.OrderByDescending(p => p.Name == "MinDamage").ToArray() }
                : f)
            .ToArray();
        var invalid = package with { Module = package.Module with { Functions = functions } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP033");
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
