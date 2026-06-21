using System.Net;

namespace DotBoxD.Hosting.Http;

internal static class SafeIpAddressClassifier
{
    public static bool IsNonGlobal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsNonGlobal(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            ? IsNonGlobalIpv4(bytes)
            : IsNonGlobalIpv6(address, bytes);
    }

    private static bool IsNonGlobalIpv4(byte[] bytes)
        => bytes[0] == 0 ||
           bytes[0] == 10 ||
           bytes[0] == 100 && bytes[1] is >= 64 and <= 127 ||
           bytes[0] == 127 ||
           bytes[0] == 169 && bytes[1] == 254 ||
           bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
           bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0 ||
           bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2 ||
           bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99 && bytes[3] == 2 ||
           bytes[0] == 192 && bytes[1] == 168 ||
           bytes[0] == 198 && bytes[1] is 18 or 19 ||
           bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
           bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113 ||
           bytes[0] >= 224;

    private static bool IsNonGlobalIpv6(IPAddress address, byte[] bytes)
        => address.Equals(IPAddress.IPv6None) ||
           address.Equals(IPAddress.IPv6Any) ||
           address.IsIPv6LinkLocal ||
           address.IsIPv6SiteLocal ||
           bytes[0] == 0xff ||
           (bytes[0] & 0xfe) == 0xfc ||
           (bytes[0] & 0xe0) != 0x20 ||
           IsIetfProtocolAssignment(bytes) ||
           IsDocumentation(bytes) ||
           IsDocumentation2(bytes) ||
           Is6To4(bytes);

    private static bool IsIetfProtocolAssignment(byte[] bytes)
        => bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] <= 0x01;

    private static bool IsDocumentation(byte[] bytes)
        => bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8;

    private static bool IsDocumentation2(byte[] bytes)
        => bytes[0] == 0x3f && (bytes[1] & 0xf0) == 0xf0;

    private static bool Is6To4(byte[] bytes)
        => bytes[0] == 0x20 && bytes[1] == 0x02;
}
