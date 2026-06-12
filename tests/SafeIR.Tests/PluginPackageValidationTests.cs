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
        var server = PluginServer.Create();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { PluginId = "other-plugin" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP011");
    }

    [Fact]
    public async Task Install_rejects_manifest_effects_that_do_not_match_verified_module()
    {
        var server = PluginServer.Create();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Effects = ["Cpu", "GameStateWrite", "Audit"] } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP041");
    }

    [Fact]
    public async Task Install_rejects_missing_kernel_entrypoint()
    {
        var server = PluginServer.Create();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Entrypoints = package.Entrypoints with { Handle = "MissingHandle" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP032");
    }

    [Fact]
    public async Task Install_rejects_subscription_kernel_that_does_not_match_module_metadata()
    {
        var server = PluginServer.Create();
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
        var server = PluginServer.Create(messages, builder => builder.UseCompilerIfAvailable(compiler));
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
        var server = PluginServer.Create();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Mode = (ExecutionMode)123 } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP042");
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
