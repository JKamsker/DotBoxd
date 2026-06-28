using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerContextContractTests
{
    [Theory]
    [InlineData(
        """
        public GameContext(HookContext raw) { }
        """,
        "constructor")]
    [InlineData(
        """
        public static GameContext FromHookContext(HookContext raw) => new();
        """,
        "FromHookContext")]
    [InlineData(
        """
        public HookContext Raw => throw new System.NotSupportedException();
        """,
        "Raw")]
    [InlineData(
        """
        public Sample.Game.IGameWorld World => throw new System.NotSupportedException();
        """,
        "World")]
    [InlineData(
        """
        public IPluginMessageSink Messages => throw new System.NotSupportedException();
        """,
        "Messages")]
    [InlineData(
        """
        public bool HasCancelableDispatch => false;
        """,
        "HasCancelableDispatch")]
    public void Context_member_colliding_with_generated_surface_reports_generation_diagnostic(
        string contextMember,
        string expectedName)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer($$"""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext
            {
                {{contextMember}}
            }
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains(expectedName, StringComparison.Ordinal) &&
                 d.GetMessage().Contains("collides with the generated context surface", StringComparison.Ordinal));
    }

    [Fact]
    public void Context_OnCreated_partial_method_remains_the_supported_creation_hook()
    {
        var generated = PluginAnalyzerGeneratedPackageFactory.GeneratedSources(MinimalServer("""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext
            {
                partial void OnCreated(HookContext raw)
                {
                }
            }
            """));

        var source = string.Join("\n", generated);

        Assert.Contains(
            "partial void OnCreated(global::DotBoxD.Abstractions.HookContext raw);",
            source,
            StringComparison.Ordinal);
    }
}
