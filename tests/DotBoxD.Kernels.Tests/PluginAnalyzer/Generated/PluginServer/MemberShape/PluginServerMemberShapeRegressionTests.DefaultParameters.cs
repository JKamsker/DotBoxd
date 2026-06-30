namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginServerMemberShapeRegressionTests
{
    [Fact]
    public void Generated_plugin_server_preserves_forwarded_method_default_parameters()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    ValueTask<int> ReadAsync(int max = 10, CancellationToken ct = default);
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains(
            "ReadAsync(int @max = 10, global::System.Threading.CancellationToken @ct = default)",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_preserves_char_and_decimal_default_parameters()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    ValueTask<int> ReadAsync(char marker = 'x', decimal weight = 1.5m);
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains("ReadAsync(char @marker = 'x', decimal @weight = 1.5m)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_preserves_metadata_style_DateTime_default_parameters()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    ValueTask<int> ReadAsync(
                        [global::System.Runtime.InteropServices.Optional,
                         global::System.Runtime.CompilerServices.DateTimeConstant(0L)]
                        global::System.DateTime when);
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains(
            "ReadAsync(global::System.DateTime @when = default)",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Generated_plugin_server_preserves_nullable_enum_default_parameters()
    {
        var (generated, outputCompilation) = PluginServerGenerationTestDriver.Run(ServerSource("""
                    ValueTask<int> ReadAsync(Mode? mode = Mode.Slow, CancellationToken ct = default);
            """, """

                public enum Mode
                {
                    Fast = 1,
                    Slow = 2
                }
            """));

        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);
        Assert.Contains(
            "ReadAsync(global::Regression.Game.Mode? @mode = unchecked((global::Regression.Game.Mode)2), " +
            "global::System.Threading.CancellationToken @ct = default)",
            generated,
            StringComparison.Ordinal);
    }
}
