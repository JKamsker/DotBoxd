using System.Net;
using System.Reflection;
using System.Text;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpResponseAccountingTests
{
    private static readonly Func<HttpResponseMessage, long> MeasureMetadataBytes = CreateMeasureDelegate();

    [Fact]
    public void MeasureMetadataBytes_counts_status_line_and_header_values_exactly()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = "Cafe \u00e9 OK",
            Version = new Version(2, 0)
        };
        Assert.True(response.Headers.TryAddWithoutValidation("X-Multi", ["one", "two"]));
        Assert.True(response.Headers.TryAddWithoutValidation("X-Trace", "snowman \u2603"));
        Assert.True(response.Content.Headers.TryAddWithoutValidation("X-Content", ["alpha", "beta"]));

        var expected = Utf8Bytes("HTTP/2.0 202 Cafe \u00e9 OK\r\n") +
                       HeaderBytes("X-Multi", "one", "two") +
                       HeaderBytes("X-Trace", "snowman \u2603") +
                       HeaderBytes("X-Content", "alpha", "beta") +
                       Utf8Bytes("\r\n");

        Assert.Equal(expected, MeasureMetadataBytes(response));
    }

    [Fact]
    public void MeasureMetadataBytes_counts_explicit_empty_reason_phrase_status_space()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new ByteArrayContent([]),
            ReasonPhrase = string.Empty
        };

        Assert.Equal(Utf8Bytes("HTTP/1.1 204 \r\n\r\n"), MeasureMetadataBytes(response));
    }

    private static long HeaderBytes(string name, params string[] values)
    {
        long bytes = 0;
        foreach (var value in values)
        {
            bytes += Utf8Bytes(name);
            bytes += Utf8Bytes(": ");
            bytes += Utf8Bytes(value);
            bytes += Utf8Bytes("\r\n");
        }

        return bytes;
    }

    private static int Utf8Bytes(string value)
        => Encoding.UTF8.GetByteCount(value);

    private static Func<HttpResponseMessage, long> CreateMeasureDelegate()
    {
        var type = typeof(DotBoxD.Hosting.Http.SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.Internal.SafeHttpResponseAccounting",
            throwOnError: true)!;
        var method = type.GetMethod("MeasureMetadataBytes", BindingFlags.Public | BindingFlags.Static)!;
        return method.CreateDelegate<Func<HttpResponseMessage, long>>();
    }
}
