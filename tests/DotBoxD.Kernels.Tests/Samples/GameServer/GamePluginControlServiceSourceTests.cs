namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GamePluginControlServiceSourceTests
{
    [Fact]
    public void InvokeServerExtensionAsync_uses_owner_checked_kernel_snapshot()
    {
        var source = File.ReadAllText(GamePluginControlServicePath());
        var method = ExtractInvokeServerExtensionAsync(source);

        Assert.DoesNotContain("_server.Kernels.Get(pluginId)", method, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(kernel.OwnerId, _session)", method, StringComparison.Ordinal);
    }

    private static string ExtractInvokeServerExtensionAsync(string source)
    {
        const string startMarker = "public async ValueTask<byte[]> InvokeServerExtensionAsync";
        const string endMarker = "public ValueTask UpdateSettingsAsync";
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);

        Assert.True(start >= 0, "InvokeServerExtensionAsync method was not found.");
        Assert.True(end > start, "UpdateSettingsAsync method marker was not found.");
        return source[start..end];
    }

    private static string GamePluginControlServicePath()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "GameServer",
            "Examples.GameServer.Server",
            "Ipc",
            "GamePluginControlService.cs"));
}
