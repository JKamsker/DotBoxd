namespace DotBoxD.Transports.Tcp;

internal static class ReceiveConcurrencyGuard
{
    public static void Enter(ref int activeReceive, string channelName)
    {
        if (Interlocked.CompareExchange(ref activeReceive, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                $"{channelName} only supports one pending receive operation at a time.");
        }
    }

    public static void Exit(ref int activeReceive) => Volatile.Write(ref activeReceive, 0);
}
