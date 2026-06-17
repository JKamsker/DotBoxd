namespace DotBoxD.Kernels.Tests.Hosting.Regression;

public sealed class Fix_API_0025_Tests
{
    [Theory]
    [InlineData("DotBoxD")]
    [InlineData("DotBoxD.Services")]
    [InlineData("DotBoxD.Services.All")]
    [InlineData("DotBoxD.Codecs.MessagePack")]
    [InlineData("DotBoxD.Transports.NamedPipes")]
    [InlineData("DotBoxD.Transports.Tcp")]
    public void Public_api_baselines_cover_services_channels_and_meta_packages(string packageId)
    {
        var path = Path.Combine(RepositoryRoot(), "docs", "api-baselines", $"{packageId}.txt");

        Assert.True(File.Exists(path), $"Missing public API baseline: {path}");
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
