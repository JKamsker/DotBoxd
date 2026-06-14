namespace SafeIR.Serialization.Json.Internal;

using SafeIR.Hosting;

public static class SandboxHostJsonExtensions
{
    public static ValueTask<SandboxModule> ImportJsonAsync(
        this SandboxHost host,
        string jsonIr,
        CancellationToken cancellationToken = default)
        => SafeIR.Serialization.Json.SandboxHostJsonExtensions.ImportJsonAsync(
            host,
            jsonIr,
            cancellationToken);
}
