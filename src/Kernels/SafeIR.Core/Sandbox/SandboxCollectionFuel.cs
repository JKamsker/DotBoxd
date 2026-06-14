namespace SafeIR;

public static class SandboxCollectionFuel
{
    private const long BaseCost = 2;

    public static bool IsCollectionIntrinsic(string callName)
        => callName is "list.empty" or "list.of" or "list.count" or "list.get" or "list.add"
            or "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove";

    public static long Empty() => BaseCost;

    public static long Read(int count = 0) => BaseCost + Math.Max(0, count);

    public static long Copy(int sourceCount, int addedCount = 0)
        => BaseCost + Math.Max(0, sourceCount) + Math.Max(0, addedCount);

    internal static long AllocationBytes(int elementCount, int bytesPerElement, bool minimumOne = false)
        => AllocationBytes((long)Math.Max(0, elementCount), bytesPerElement, minimumOne);

    internal static long AllocationBytes(
        int sourceCount,
        int addedCount,
        int bytesPerElement,
        bool minimumOne = false)
        => AllocationBytes(
            (long)Math.Max(0, sourceCount) + Math.Max(0, addedCount),
            bytesPerElement,
            minimumOne);

    public static long EstimateCall(string callName, int argumentCount)
        => callName switch
        {
            "list.empty" or "map.empty" => Empty(),
            "list.of" => Copy(argumentCount),
            "list.count" or "list.get" or "map.containsKey" or "map.get" => Read(),
            "list.add" or "map.set" => Copy(argumentCount, addedCount: 1),
            "map.remove" => Copy(argumentCount),
            _ => 0
        };

    private static long AllocationBytes(long elementCount, int bytesPerElement, bool minimumOne)
    {
        var chargedElements = minimumOne ? Math.Max(1L, elementCount) : elementCount;
        try
        {
            return checked(chargedElements * bytesPerElement);
        }
        catch (OverflowException)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "collection copy allocation budget exhausted"));
        }
    }
}
