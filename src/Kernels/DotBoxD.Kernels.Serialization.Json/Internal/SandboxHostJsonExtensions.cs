namespace DotBoxD.Kernels.Serialization.Json.Internal;

internal static class SandboxHostJsonExtensions
{
    public static ValueTask<SandboxModule> ImportJsonAsync(
        this DotBoxD.Hosting.Execution.SandboxHost host,
        string jsonIr,
        CancellationToken cancellationToken = default)
        => Hosting.SandboxHostJsonExtensions.ImportJsonAsync(
            host,
            jsonIr,
            cancellationToken);
}
