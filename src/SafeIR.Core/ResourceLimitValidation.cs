namespace SafeIR;

internal static class ResourceLimitValidation
{
    public static void Validate(ResourceLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ThrowIfNegative(limits.MaxFuel, nameof(ResourceLimits.MaxFuel));
        ThrowIfNegative(limits.MaxAllocatedBytes, nameof(ResourceLimits.MaxAllocatedBytes));
        ThrowIfNegative(limits.MaxCallDepth, nameof(ResourceLimits.MaxCallDepth));
        ThrowIfNegative(limits.MaxHostCalls, nameof(ResourceLimits.MaxHostCalls));
        ThrowIfNegative(limits.MaxListLength, nameof(ResourceLimits.MaxListLength));
        ThrowIfNegative(limits.MaxMapEntries, nameof(ResourceLimits.MaxMapEntries));
        ThrowIfNegative(limits.MaxCollectionDepth, nameof(ResourceLimits.MaxCollectionDepth));
        ThrowIfNegative(limits.MaxTotalCollectionElements, nameof(ResourceLimits.MaxTotalCollectionElements));
        ThrowIfNegative(limits.MaxFileBytesRead, nameof(ResourceLimits.MaxFileBytesRead));
        ThrowIfNegative(limits.MaxFileBytesWritten, nameof(ResourceLimits.MaxFileBytesWritten));
        ThrowIfNegative(limits.MaxNetworkBytesRead, nameof(ResourceLimits.MaxNetworkBytesRead));
        ThrowIfNegative(limits.MaxLogEvents, nameof(ResourceLimits.MaxLogEvents));
        ThrowIfNegative(limits.MaxLogMessageLength, nameof(ResourceLimits.MaxLogMessageLength));
        ThrowIfNegative(limits.MaxStringLength, nameof(ResourceLimits.MaxStringLength));
        ThrowIfNegative(limits.MaxTotalStringBytes, nameof(ResourceLimits.MaxTotalStringBytes));
        if (limits.MaxWallTime is not null && limits.MaxWallTime.Value < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(ResourceLimits.MaxWallTime));
        }
    }

    private static void ThrowIfNegative(long value, string paramName)
    {
        if (value < 0) {
            throw new ArgumentOutOfRangeException(paramName);
        }
    }
}
