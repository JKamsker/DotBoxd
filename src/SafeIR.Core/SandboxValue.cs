namespace SafeIR;

public abstract record SandboxValue
{
    public static SandboxValue Unit { get; } = new UnitValue();

    public abstract SandboxType Type { get; }

    public static SandboxValue FromBool(bool value) => new BoolValue(value);

    public static SandboxValue FromInt32(int value) => new I32Value(value);

    public static SandboxValue FromInt64(long value) => new I64Value(value);

    public static SandboxValue FromDouble(double value) => new F64Value(value);

    public static SandboxValue FromString(string value) => new StringValue(value);

    public static SandboxValue FromPath(string value) => new SandboxPathValue(new SandboxPath(value));

    public static SandboxValue FromUri(string value) => new SandboxUriValue(new SandboxUri(value));

    public static SandboxValue FromList(IReadOnlyList<SandboxValue> values) => new ListValue(values);
}

public sealed record UnitValue : SandboxValue
{
    public override SandboxType Type => SandboxType.Unit;
}

public sealed record BoolValue(bool Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.Bool;
}

public sealed record I32Value(int Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.I32;
}

public sealed record I64Value(long Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.I64;
}

public sealed record F64Value(double Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.F64;
}

public sealed record StringValue(string Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.String;
}

public sealed record SandboxPath(string RelativePath)
{
    public override string ToString() => RelativePath;
}

public sealed record SandboxPathValue(SandboxPath Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.SandboxPath;
}

public sealed record SandboxUri(string Value)
{
    public override string ToString() => Value;
}

public sealed record SandboxUriValue(SandboxUri Value) : SandboxValue
{
    public override SandboxType Type => SandboxType.Scalar("SandboxUri");
}

public sealed record ListValue(IReadOnlyList<SandboxValue> Values) : SandboxValue
{
    public override SandboxType Type
        => Values.Count == 0 ? SandboxType.List(SandboxType.Unit) : SandboxType.List(Values[0].Type);
}
