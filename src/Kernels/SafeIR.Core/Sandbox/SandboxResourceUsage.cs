namespace SafeIR;

public sealed record SandboxResourceUsage(
    long FuelUsed,
    long MaxFuel,
    long LoopIterations,
    long AllocatedBytes,
    int HostCalls,
    long FileBytesRead,
    long FileBytesWritten,
    long NetworkBytesRead,
    long NetworkBytesWritten,
    int LogEvents,
    long CollectionElements,
    long StringBytes);
