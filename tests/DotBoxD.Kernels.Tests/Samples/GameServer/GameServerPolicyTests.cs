using System.Reflection;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GameServerPolicyTests
{
    [Fact]
    public void Policy_does_not_grant_undeclared_monster_write_capabilities()
    {
        var policy = InvokePolicy("ForKernel", "host.message.write");

        Assert.False(policy.GrantsCapability("game.world.monster.write.kill"));
        Assert.DoesNotContain(
            policy.Grants,
            grant => grant.Id.StartsWith("game.world.monster.write.", StringComparison.Ordinal));
    }

    [Fact]
    public void Policy_grants_declared_monster_write_capabilities()
    {
        var policy = InvokePolicy("ForKernel", "game.world.monster.write.kill");

        Assert.True(policy.GrantsCapability("game.world.monster.write.kill"));
    }

    private static SandboxPolicy InvokePolicy(string methodName, params string[] requiredCapabilities)
    {
        var gameServer = Assembly.LoadFrom(GameServerAssemblyPath());
        var serverPolicy = gameServer.GetType("DotBoxD.Kernels.Game.Server.ServerPolicy", throwOnError: true)!;
        var result = serverPolicy
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [requiredCapabilities]);
        return (SandboxPolicy)result!;
    }

    private static string GameServerAssemblyPath()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configuration = output.Parent!.Name;
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "GameServer",
            "Examples.GameServer.Server",
            "bin",
            configuration,
            "net10.0",
            "Examples.GameServer.Server.dll"));
    }
}
