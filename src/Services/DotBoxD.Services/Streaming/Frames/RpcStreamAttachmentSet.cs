namespace DotBoxD.Services.Streaming.Frames;

internal readonly struct RpcStreamAttachmentSet
{
    private readonly RpcStreamAttachment? _single;
    private readonly RpcStreamAttachment[]? _many;

    private RpcStreamAttachmentSet(RpcStreamAttachment single)
    {
        _single = single;
        _many = null;
    }

    private RpcStreamAttachmentSet(RpcStreamAttachment[] many)
    {
        _single = null;
        _many = many;
    }

    public static RpcStreamAttachmentSet Empty => default;

    public bool IsEmpty => Count == 0;

    public bool IsSingle => _single is not null;

    public int Count => _many?.Length ?? (_single is null ? 0 : 1);

    public RpcStreamAttachment Single =>
        _single ?? throw new InvalidOperationException("Attachment set does not contain a single stream.");

    public RpcStreamAttachment[] Many =>
        _many ?? throw new InvalidOperationException("Attachment set is not array-backed.");

    public static RpcStreamAttachmentSet FromSingle(RpcStreamAttachment stream)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        return new RpcStreamAttachmentSet(stream);
    }

    public static RpcStreamAttachmentSet FromArray(RpcStreamAttachment[]? streams)
    {
        if (streams is null || streams.Length == 0)
        {
            return Empty;
        }

        return new RpcStreamAttachmentSet(streams);
    }

    public RpcStreamAttachment? GetAt(int index)
    {
        if (_many is not null)
        {
            return _many[index];
        }

        if (index == 0)
        {
            return _single;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }
}
