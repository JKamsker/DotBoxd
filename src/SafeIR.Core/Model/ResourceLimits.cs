namespace SafeIR;

public sealed record ResourceLimits(
    long MaxFuel = 100_000,
    long MaxLoopIterations = 100_000,
    TimeSpan? MaxWallTime = null,
    long MaxAllocatedBytes = 1_048_576,
    int MaxCallDepth = 64,
    int MaxHostCalls = 100,
    int MaxListLength = 10_000,
    int MaxMapEntries = 10_000,
    int MaxCollectionDepth = 32,
    long MaxTotalCollectionElements = 100_000,
    long MaxFileBytesRead = 1_048_576,
    long MaxFileBytesWritten = 0,
    long MaxNetworkBytesRead = 1_048_576,
    long MaxNetworkBytesWritten = 1_048_576,
    int MaxLogEvents = 100,
    int MaxLogMessageLength = 4_096,
    int MaxStringLength = 65_536,
    long MaxTotalStringBytes = 1_048_576)
{
    public TimeSpan EffectiveWallTime => MaxWallTime ?? TimeSpan.FromMilliseconds(100);
}
