using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginServerApiGuardTests
{
    [Fact]
    public async Task InstallAsync_rejects_null_package()
    {
        using var server = PluginServer.Create();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => server.InstallAsync(null!).AsTask());

        Assert.Equal("package", exception.ParamName);
    }

    [Fact]
    public async Task InstallPoolAsync_rejects_null_package()
    {
        using var server = PluginServer.Create();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => server.InstallPoolAsync(null!, degreeOfParallelism: 1).AsTask());

        Assert.Equal("package", exception.ParamName);
    }

    [Fact]
    public async Task InstallServerExtensionAsync_rejects_null_package()
    {
        using var server = PluginServer.Create();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => server.InstallServerExtensionAsync(null!).AsTask());

        Assert.Equal("package", exception.ParamName);
    }
}
