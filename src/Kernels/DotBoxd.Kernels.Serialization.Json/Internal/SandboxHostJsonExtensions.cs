namespace DotBoxd.Kernels.Serialization.Json.Internal;

using DotBoxd.Hosting;

public static class SandboxHostJsonExtensions
{
    public static ValueTask<SandboxModule> ImportJsonAsync(
        this SandboxHost host,
        string jsonIr,
        CancellationToken cancellationToken = default)
        => DotBoxd.Kernels.Serialization.Json.SandboxHostJsonExtensions.ImportJsonAsync(
            host,
            jsonIr,
            cancellationToken);
}
