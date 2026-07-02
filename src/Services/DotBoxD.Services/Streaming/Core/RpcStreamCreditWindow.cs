using System.Collections.Concurrent;

namespace DotBoxD.Services.Streaming.Core;

internal static class RpcStreamCreditWindow
{
    public static bool IsValidCount(int count) =>
        count is > 0 and <= RpcStreamManager.WindowSize;

    public static bool TryBufferReserved(
        ConcurrentDictionary<int, int> pendingCredits,
        ConcurrentDictionary<int, RpcStreamSendState> senders,
        ConcurrentDictionary<int, byte> reservedOutbound,
        int streamId,
        int count)
    {
        while (true)
        {
            if (!reservedOutbound.ContainsKey(streamId))
            {
                return TryCreditActiveOrPrune(pendingCredits, senders, streamId, count);
            }

            if (pendingCredits.TryGetValue(streamId, out var current))
            {
                if (current > RpcStreamManager.WindowSize - count)
                {
                    return false;
                }

                if (pendingCredits.TryUpdate(streamId, current + count, current))
                {
                    break;
                }
            }
            else if (pendingCredits.TryAdd(streamId, count))
            {
                break;
            }
        }

        return FlushIfRegistered(pendingCredits, senders, reservedOutbound, streamId);
    }

    private static bool TryCreditActiveOrPrune(
        ConcurrentDictionary<int, int> pendingCredits,
        ConcurrentDictionary<int, RpcStreamSendState> senders,
        int streamId,
        int count)
    {
        if (senders.TryGetValue(streamId, out var state))
        {
            return state.AddCredit(count);
        }

        pendingCredits.TryRemove(streamId, out _);
        return true;
    }

    private static bool FlushIfRegistered(
        ConcurrentDictionary<int, int> pendingCredits,
        ConcurrentDictionary<int, RpcStreamSendState> senders,
        ConcurrentDictionary<int, byte> reservedOutbound,
        int streamId)
    {
        if (senders.TryGetValue(streamId, out var state) &&
            pendingCredits.TryRemove(streamId, out var pending))
        {
            return state.AddCredit(pending);
        }

        if (!reservedOutbound.ContainsKey(streamId))
        {
            pendingCredits.TryRemove(streamId, out _);
        }

        return true;
    }
}
