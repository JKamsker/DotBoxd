using static SafeIR.Tests.NetworkTestFixtures;

namespace SafeIR.Tests;

public sealed class BindingResourceFuelTests
{
    [Fact]
    public async Task File_read_charges_base_fuel_once_plus_raw_bytes()
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "config.txt"), "hello");

        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(InterpreterAndPolicyTests.FileReadJson("config.txt"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantFileRead(temp.Path, 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(59, result.ResourceUsage.FuelUsed);
    }

    [Fact]
    public async Task Http_get_charges_base_fuel_once_plus_raw_bytes()
    {
        var host = SandboxTestHost.Create(networkInvoker: FakeInvoker("hello"));
        var module = await host.ParseJsonAsync(NetworkJson("https://api.example.com/config"));
        var policy = SandboxPolicyBuilder.Create()
            .GrantHttpGet(["api.example.com"], maxResponseBytes: 1024)
            .WithFuel(5_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(84, result.ResourceUsage.FuelUsed);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "safe-ir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
