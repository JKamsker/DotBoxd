namespace SafeIR;

internal readonly record struct ValueShape(
    long Elements,
    int MaxListLength,
    int MaxMapEntries,
    int Depth,
    int MaxStringLength,
    long StringBytes)
{
    public ValueShape Combine(ValueShape nested)
        => new(
            Elements + nested.Elements,
            Math.Max(MaxListLength, nested.MaxListLength),
            Math.Max(MaxMapEntries, nested.MaxMapEntries),
            Math.Max(Depth, nested.Depth == 0 ? Depth : nested.Depth + 1),
            Math.Max(MaxStringLength, nested.MaxStringLength),
            StringBytes + nested.StringBytes);
}
