namespace DotBoxD.Kernels.Tests.Plugins.Documentation;

public sealed class PluginAdapterDocumentationTests
{
    [Fact]
    public void Addendum_examples_point_to_the_maintained_game_server_sample()
    {
        var examples = ReadRepositoryText("docs/Specs/Addendum/Examples.md");

        Assert.Contains("samples/GameServer/Examples.GameServer.Server", examples);
        Assert.Contains("Examples.GameServer.Plugin", examples);
        Assert.Contains("Examples.GameServer.Plugin.Client", examples);
        Assert.Contains("The host still imports the JSON and derives capabilities from the IR", examples);
    }

    [Fact]
    public void Game_server_sample_uses_explicit_event_contracts_and_message_policy()
    {
        var contracts = ReadRepositoryText(
            "samples/GameServer/Examples.GameServer.Server.Abstractions/ServiceContracts.cs");
        var policy = ReadRepositoryText(
            "samples/GameServer/Examples.GameServer.Server/ServerPolicy.cs");

        Assert.Contains("IEventKernel<MonsterAggroEvent>", contracts);
        Assert.Contains("IEventKernel<AttackEvent>", contracts);
        Assert.Contains("GrantHostMessageWrite", policy);
        Assert.Contains("game.world.monster.read.*", policy);
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"Missing repository file: {path}");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
