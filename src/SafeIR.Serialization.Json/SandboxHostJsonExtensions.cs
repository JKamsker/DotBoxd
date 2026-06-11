namespace SafeIR;

using SafeIR.Hosting;

public static class SandboxHostJsonExtensions
{
    public static ValueTask<SandboxModule> ImportJsonAsync(
        this SandboxHost host,
        string jsonIr,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(SafeIrJsonImporter.Import(jsonIr));
    }

    public static ValueTask<SandboxModule> ParseJsonAsync(
        this SandboxHost host,
        string jsonIr,
        CancellationToken cancellationToken = default)
        => host.ImportJsonAsync(jsonIr, cancellationToken);
}
