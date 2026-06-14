namespace SafeIR.Benchmarks.Ipc;

using System.Buffers;

internal sealed class ReusableBufferWriter : IBufferWriter<byte>
{
    private byte[] _buffer;
    private int _written;

    public ReusableBufferWriter(int capacity)
    {
        _buffer = new byte[capacity];
    }

    public int WrittenCount => _written;

    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public void Reset() => _written = 0;

    public void Advance(int count)
    {
        if (count < 0 || _written + count > _buffer.Length) {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    private void EnsureCapacity(int sizeHint)
    {
        var required = _written + Math.Max(sizeHint, 1);
        if (required <= _buffer.Length) {
            return;
        }

        Array.Resize(ref _buffer, Math.Max(required, _buffer.Length * 2));
    }
}
