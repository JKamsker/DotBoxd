using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// ALG-0018: direct plugin kernel dispatch revalidated the adapter/entrypoint shape
/// on every <see cref="InstalledKernel.ShouldHandleAsync{TEvent}"/> and
/// <see cref="InstalledKernel.HandleAsync{TEvent}"/> call. The manifest, execution
/// plan, and entrypoints are immutable for the lifetime of a kernel, so a successful
/// validation for a stable adapter instance is cached by identity and shape. These tests
/// assert the cache reuses full validation while still checking current adapter shape on
/// every dispatch and failing closed for new, invalid, or mutated adapters.
/// </summary>
public sealed class Fix_ALG_0018_Tests
{
    [Fact]
    public async Task Repeated_direct_dispatch_checks_adapter_shape_each_time()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new CountingDamageEventAdapter();
        var e = new DamageEvent("fire", 120, "player-1");

        var first = await kernel.ShouldHandleAsync(adapter, e);
        var validationsAfterFirst = adapter.ParametersAccessCount;

        await kernel.ShouldHandleAsync(adapter, e);
        await kernel.HandleAsync(adapter, e);
        await kernel.ShouldHandleAsync(adapter, e);

        Assert.True(first);
        // First call performs full validation (reads the adapter parameter shape once).
        Assert.Equal(1, validationsAfterFirst);
        // Subsequent calls reuse full validation, but still re-read shape to catch mutation.
        Assert.Equal(4, adapter.ParametersAccessCount);
    }

    [Fact]
    public async Task New_adapter_instance_is_revalidated_fail_closed()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var e = new DamageEvent("fire", 120, "player-1");

        var firstAdapter = new CountingDamageEventAdapter();
        await kernel.ShouldHandleAsync(firstAdapter, e);
        Assert.Equal(1, firstAdapter.ParametersAccessCount);

        // A different instance is not in the per-identity cache, so it is revalidated.
        var secondAdapter = new CountingDamageEventAdapter();
        await kernel.ShouldHandleAsync(secondAdapter, e);
        Assert.Equal(1, secondAdapter.ParametersAccessCount);
    }

    [Fact]
    public async Task Invalid_adapter_still_fails_closed_without_caching_success()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new UnsubscribedDamageEventAdapter();
        var e = new DamageEvent("fire", 120, "player-1");

        var first = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.ShouldHandleAsync(adapter, e));
        Assert.Contains(first.Diagnostics, d => d.Code == "SGP031");

        // Failed validation must not be cached: the second call still fails closed.
        var second = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.HandleAsync(adapter, e));
        Assert.Contains(second.Diagnostics, d => d.Code == "SGP031");
        Assert.Empty(kernel.ExecutionObservations);
    }

    [Fact]
    public async Task Mutated_adapter_shape_after_success_is_revalidated_fail_closed()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new MutableDamageEventAdapter();
        var e = new DamageEvent("fire", 120, "player-1");

        Assert.True(await kernel.ShouldHandleAsync(adapter, e));
        adapter.DropTargetParameter = true;

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.ShouldHandleAsync(adapter, e));

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP033");
        Assert.Single(kernel.ExecutionObservations);
    }

    [Fact]
    public async Task Hook_dispatch_revalidates_adapter_shape_after_registration()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        var adapter = new MutableDamageEventAdapter();
        server.Hooks.On(adapter).UseKernel(kernel);
        adapter.DropTargetParameter = true;

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1")));

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP033");
        Assert.Empty(kernel.ExecutionObservations);
    }

    private sealed class CountingDamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        private readonly IReadOnlyList<Parameter> _parameters = [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public int ParametersAccessCount { get; private set; }

        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters
        {
            get
            {
                ParametersAccessCount++;
                return _parameters;
            }
        }

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }

    private sealed class MutableDamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        private static readonly IReadOnlyList<Parameter> ValidParameters = [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        private static readonly IReadOnlyList<Parameter> InvalidParameters = [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32)
        ];

        public bool DropTargetParameter { get; set; }

        public string EventName => "DamageEvent";

        public IReadOnlyList<Parameter> Parameters
            => DropTargetParameter ? InvalidParameters : ValidParameters;

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }

    private sealed class UnsubscribedDamageEventAdapter : IPluginEventAdapter<DamageEvent>
    {
        public string EventName => "AdminEvent";

        public IReadOnlyList<Parameter> Parameters { get; } = [
            new("e_DamageType", SandboxType.String),
            new("e_Amount", SandboxType.I32),
            new("e_TargetId", SandboxType.String)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(DamageEvent e)
            => [
                SandboxValue.FromString(e.DamageType),
                SandboxValue.FromInt32(e.Amount),
                SandboxValue.FromString(e.TargetId)
            ];
    }
}
