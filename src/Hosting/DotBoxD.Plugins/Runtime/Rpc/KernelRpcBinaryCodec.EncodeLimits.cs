namespace DotBoxD.Plugins;

using System.Buffers;

public static partial class KernelRpcBinaryCodec
{
    private static void WriteItems(
        IBufferWriter<byte> writer,
        ReadOnlySpan<KernelRpcValue> items,
        int depth,
        ref int itemCount)
    {
        var nextDepth = EnterEncodeCollection(depth);
        ReserveEncodeItems(items.Length, ref itemCount);
        WriteLength(writer, items.Length);
        foreach (var item in items)
        {
            WriteValue(writer, item, nextDepth, ref itemCount);
        }
    }

    private static int EnterEncodeCollection(int depth)
    {
        var nextDepth = depth + 1;
        if (nextDepth > MaxDecodeDepth)
        {
            throw new ArgumentException("Server extension payload exceeds the maximum nesting depth.");
        }

        return nextDepth;
    }

    private static void ReserveEncodeItems(int count, ref int itemCount)
    {
        if (count < 0 || itemCount > MaxDecodeItems - count)
        {
            throw new ArgumentException("Server extension payload contains too many items.");
        }

        itemCount += count;
    }
}
