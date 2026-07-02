using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerContextContractTests
{
    [Theory]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext))]
        public partial class RemotePluginServer : Sample.Game.IGameWorld;

        public sealed class GameContext;
        """,
        "must be declared partial")]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext<int>))]
        public partial class RemotePluginServer : Sample.Game.IGameWorld;

        public sealed partial class GameContext<T>;
        """,
        "must be a non-generic, non-nested partial class")]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(Outer.GameContext))]
        public partial class RemotePluginServer : Sample.Game.IGameWorld;

        public sealed partial class Outer
        {
            public sealed partial class GameContext;
        }
        """,
        "must be a non-generic, non-nested partial class")]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext))]
        public partial class RemotePluginServer : Sample.Game.IGameWorld;

        internal sealed partial class GameContext;
        """,
        "must be public because")]
    public void Invalid_context_shape_reports_generation_diagnostic(string source, string expectedMessage)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer(source));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void File_local_context_reports_generation_diagnostic_without_raw_compiler_errors()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer("""
            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            internal partial class RemotePluginServer : Sample.Game.IGameWorld;

            file sealed partial class RemotePluginContext
            {
                partial void OnCreated(global::DotBoxD.Abstractions.HookContext raw)
                {
                }
            }
            """));

        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("RemotePluginContext", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("file-local", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext), ContextFactory = "Missing")]
        public partial class RemotePluginServer : Sample.Game.IGameWorld;

        public sealed partial class GameContext;
        """,
        "was not found")]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext), ContextFactory = nameof(GameContext.Create))]
        public partial class RemotePluginServer : Sample.Game.IGameWorld;

        public sealed partial class GameContext
        {
            public GameContext Create(HookContext raw) => this;
        }
        """,
        "must be static and have signature")]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext), ContextFactory = nameof(GameContext.Create))]
        public partial class RemotePluginServer : Sample.Game.IGameWorld;

        public sealed partial class GameContext
        {
            public static string Create(HookContext raw) => "";
        }
        """,
        "must be static and have signature")]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext), ContextFactory = nameof(GameContext.Create))]
        public partial class RemotePluginServer : Sample.Game.IGameWorld;

        public sealed partial class GameContext
        {
            public static GameContext Create(HookContext raw) => new();
            public static GameContext Create(string raw) => new();
        }
        """,
        "must not be overloaded")]
    [InlineData(
        """
        [GeneratePluginServer(Context = typeof(GameContext), ContextFactory = nameof(GameContext.Create))]
        public partial class RemotePluginServer : Sample.Game.IGameWorld;

        public sealed partial class GameContext
        {
            public static GameContext Create(string raw) => new();
        }
        """,
        "must be static and have signature")]
    public void Invalid_context_factory_reports_generation_diagnostic(string source, string expectedMessage)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(MinimalServer(source));

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains(expectedMessage, StringComparison.Ordinal));
    }
}
