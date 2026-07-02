using DotBoxD.Kernels.Policies;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginSessionOwnerDisposalTests
{
    [Fact]
    public async Task Session_ownership_methods_reject_after_owning_server_disposal()
    {
        using var server = PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        await session.InstallAsync(FireDamagePluginPackage.Create());

        server.Dispose();

        var failures = new[]
        {
            OperationFailure("Owns", Record.Exception(() => session.Owns("fire-damage"))),
            OperationFailure(
                "TryGetOwned",
                Record.Exception(() => session.TryGetOwned("fire-damage", out _))),
            OperationFailure("Uninstall", Record.Exception(() => session.Uninstall("fire-damage"))),
            OperationFailure(
                "UpdateSettingsAsync",
                await Record.ExceptionAsync(async () => await session.UpdateSettingsAsync(
                    "fire-damage",
                    new Dictionary<string, object?> { ["DamageType"] = "ice" }).AsTask())),
        };

        Assert.Equal(
            [
                "Owns:ObjectDisposedException",
                "TryGetOwned:ObjectDisposedException",
                "Uninstall:ObjectDisposedException",
                "UpdateSettingsAsync:ObjectDisposedException",
            ],
            failures);
    }

    private static string OperationFailure(string operation, Exception? exception)
        => $"{operation}:{exception?.GetType().Name ?? "none"}";

    private static SandboxPolicy LongWallPluginPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
