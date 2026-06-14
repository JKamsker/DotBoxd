using System.Text;
using System.Threading;

namespace ShaRPC.Serializers.MessagePack;

internal static class RpcRequestNameCache
{
    private const int MaxEntries = 128;
    private const int MaxCachedUtf8Bytes = 256;

    private static readonly object Gate = new();
    private static Entry[] _entries = Array.Empty<Entry>();

    public static void Register(string? value)
    {
        if (value is null)
        {
            return;
        }

        GetOrAdd(value);
    }

    public static string GetOrAdd(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaxCachedUtf8Bytes)
        {
            return Encoding.UTF8.GetString(utf8);
        }

        var entries = Volatile.Read(ref _entries);
        for (var i = 0; i < entries.Length; i++)
        {
            if (utf8.SequenceEqual(entries[i].Utf8))
            {
                return entries[i].Value;
            }
        }

        return GetOrAdd(Encoding.UTF8.GetString(utf8));
    }

    public static string GetOrAdd(string value)
    {
        if (!CanCache(value))
        {
            return value;
        }

        var entries = Volatile.Read(ref _entries);
        for (var i = 0; i < entries.Length; i++)
        {
            if (string.Equals(value, entries[i].Value, StringComparison.Ordinal))
            {
                return entries[i].Value;
            }
        }

        lock (Gate)
        {
            entries = Volatile.Read(ref _entries);
            for (var i = 0; i < entries.Length; i++)
            {
                if (string.Equals(value, entries[i].Value, StringComparison.Ordinal))
                {
                    return entries[i].Value;
                }
            }

            if (entries.Length >= MaxEntries)
            {
                return value;
            }

            var next = new Entry[entries.Length + 1];
            Array.Copy(entries, next, entries.Length);
            next[entries.Length] = new Entry(value, Encoding.UTF8.GetBytes(value));
            Volatile.Write(ref _entries, next);
            return value;
        }
    }

    private static bool CanCache(string value) =>
        value.Length <= MaxCachedUtf8Bytes &&
        Encoding.UTF8.GetByteCount(value) <= MaxCachedUtf8Bytes;

    private readonly struct Entry
    {
        public Entry(string value, byte[] utf8)
        {
            Value = value;
            Utf8 = utf8;
        }

        public string Value { get; }

        public byte[] Utf8 { get; }
    }
}
