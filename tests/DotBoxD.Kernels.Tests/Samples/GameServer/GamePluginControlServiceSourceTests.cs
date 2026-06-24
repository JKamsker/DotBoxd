namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GamePluginControlServiceSourceTests
{
    [Fact]
    public void InvokeServerExtensionAsync_is_owner_checked_before_dispatch()
    {
        var source = File.ReadAllText(GamePluginControlServicePath());
        var method = ExtractInvokeServerExtensionAsync(source);

        // The host must gate on session ownership, never reach for the kernel by id without an owner check.
        Assert.DoesNotContain("_server.Kernels.Get(pluginId)", method, StringComparison.Ordinal);
        Assert.Contains("_session.Owns(pluginId)", method, StringComparison.Ordinal);
    }

    private static string ExtractInvokeServerExtensionAsync(string source)
    {
        const string startMarker = "ValueTask<byte[]> InvokeServerExtensionAsync";
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
