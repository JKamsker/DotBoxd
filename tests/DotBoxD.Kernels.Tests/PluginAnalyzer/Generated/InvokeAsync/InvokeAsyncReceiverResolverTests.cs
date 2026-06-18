using DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsync;

public sealed class InvokeAsyncReceiverResolverTests
{
    [Theory]
    [InlineData("IGameServer", true)]
    [InlineData("IInventoryServer", true)]
    [InlineData("GameServer", false)]
    [InlineData("I", false)]
    [InlineData("IServer", false)]
    [InlineData("IGameClient", false)]
    [InlineData("Server", false)]
    public void Generated_server_interface_name_candidate_matches_generated_shape(
        string name,
        bool expected)
        => Assert.Equal(expected, InvokeAsyncReceiverResolver.IsGeneratedServerInterfaceNameCandidate(name));
}
