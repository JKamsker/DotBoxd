using System.Text;

namespace DotBoxD.Hosting.Http.Internal;

internal static class SafeHttpRequestAccounting
{
    public static long MeasureGetRequestBytes(Uri uri)
        => 4L + Encoding.UTF8.GetByteCount(uri.AbsoluteUri);
}
