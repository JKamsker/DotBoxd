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
        private readonly HookContext _raw = null!;
        """,
        "_raw")]
    [InlineData(
        """
        public CancellationToken CancellationToken => default;
        """,
        "CancellationToken")]
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
    public void Inherited_context_member_colliding_with_generated_surface_reports_generation_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer("""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public class BaseContext
            {
                public HookContext Raw => throw new System.NotSupportedException();
            }

            public sealed partial class GameContext : BaseContext;
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains("Raw", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("collides with the generated context surface", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        """
        public void OnCreated()
        {
        }
        """)]
    [InlineData(
        """
        public void OnCreated(HookContext raw)
        {
        }
        """)]
    public void Context_OnCreated_wrong_shape_reports_generation_diagnostic(string onCreated)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer($$"""
            [GeneratePluginServer(Context = typeof(GameContext))]
            public partial class RemotePluginServer : Sample.Game.IGameWorld;

            public sealed partial class GameContext
            {
                {{onCreated}}
            }
            """));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.Severity == DiagnosticSeverity.Error &&
                 d.GetMessage().Contains("OnCreated", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("partial void", StringComparison.Ordinal));
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
