using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class InvokeAsyncGeneratedReceiverSurpriseTests
{
    [Fact]
    public void Member_access_InvokeAsync_inside_generated_facade_respects_user_overload()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public ValueTask<int> InvokeAsync(
                    Func<IGameWorldAccess, ValueTask<int>> lambda,
                    CancellationToken cancellationToken = default)
                    => new(7);

                public ValueTask<int> Probe()
                    => this.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InvokeAsync_on_subclass_of_generated_facade_reports_or_lowers()
    {
        var result = RunGenerator(UsageSource("""
            public sealed class DerivedServer : RemotePluginServer
            {
                public DerivedServer(DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control)
                    : base(control)
                {
                }
            }

            public static ValueTask<int> Run(DerivedServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "DBXK100") ||
            source.Contains("AnonymousInvokeAsync", StringComparison.Ordinal),
            source);
    }

    [Fact]
    public void Same_compilation_generated_services_receiver_lowers_InvokeAsync()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.Services.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_services_receiver_on_subclass_lowers_InvokeAsync()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class DerivedServer : RemotePluginServer
            {
                public DerivedServer(DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control)
                    : base(control)
                {
                }
            }

            public static ValueTask<int> Run(DerivedServer kernels)
                => kernels.Services.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_services_receiver_on_subclass_with_hidden_services_member_reports_erased_surface()
    {
        var result = RunGenerator(UsageSource("""
            public sealed class ShadowPluginServer : IPluginServer<IGameWorldAccess>
            {
                public ValueTask StartAsync(CancellationToken cancellationToken = default) => default;

                public ValueTask RunAsync(CancellationToken cancellationToken = default) => default;

                public ValueTask<TReturn> InvokeAsync<TReturn>(
                    Func<IGameWorldAccess, ValueTask<TReturn>> lambda,
                    CancellationToken cancellationToken = default)
                    => default;

                public ValueTask<TReturn> InvokeAsync<TCaptures, TReturn>(
                    TCaptures captures,
                    RemoteServerInvocation<IGameWorldAccess, TCaptures, TReturn> lambda,
                    CancellationToken cancellationToken = default)
                    where TCaptures : class
                    => default;

                public ValueTask HoldUntilShutdownAsync(CancellationToken cancellationToken = default) => default;
            }

            public sealed class DerivedServer : RemotePluginServer
            {
                public DerivedServer(DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control)
                    : base(control)
                {
                }

                public new IPluginServer<IGameWorldAccess> Services { get; } = new ShadowPluginServer();
            }

            public static ValueTask<int> Run(DerivedServer kernels)
                => kernels.Services.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("erased IPluginServer", StringComparison.Ordinal));
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InvokeAsync_on_subclass_with_hidden_services_member_uses_generated_base_surface()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class ShadowServices;

            public sealed class DerivedServer : RemotePluginServer
            {
                public DerivedServer(DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control)
                    : base(control)
                {
                }

                public new ShadowServices Services { get; } = new();
            }

            public static ValueTask<int> Run(DerivedServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
        Assert.Contains("this global::DotBoxD.Kernels.Game.Plugin.Client.RemotePluginServer server", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_builder_tuple_receiver_lowers_InvokeAsync()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(
                DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control)
            {
                var pair = (Server: RemotePluginServerBuilder.FromConnection(control).Build(), Ignored: 0);
                return pair.Server.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}
