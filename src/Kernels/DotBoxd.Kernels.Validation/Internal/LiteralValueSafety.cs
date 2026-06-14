namespace DotBoxd.Kernels.Validation.Internal;

using DotBoxd.Kernels;

internal static class LiteralValueSafety
{
    private const int MaxTextLiteralLength = 65_536;

    public static bool Validate(SandboxValue value)
    {
        var allocates = false;
        foreach (var current in Flatten(value))
        {
            allocates |= ValidateScalar(current);
        }

        return allocates;
    }

    public static bool ContainsDangerousReference(SandboxValue value)
    {
        foreach (var current in Flatten(value))
        {
            var text = current switch
            {
                StringValue item => item.Value,
                OpaqueIdValue item => item.Value,
                SandboxPathValue item => item.Value.RelativePath,
                SandboxUriValue item => item.Value.Value,
                _ => null
            };

            if (text is not null && DangerousReferenceDetector.IsDangerousReference(text))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValidateScalar(SandboxValue value)
    {
        switch (value)
        {
            case StringValue text:
                EnsureTextLiteralLength(text.Value, "string");
                return true;
            case OpaqueIdValue id:
                EnsureTextLiteralLength(id.Value, id.TypeName);
                if (!SandboxLiteralConstraints.IsOpaqueId(id.Value) ||
                    !SandboxType.IsKnownOpaqueId(id.TypeName))
                {
                    throw InvalidLiteral("E-CONST-ID", "opaque ID constant is invalid");
                }

                return true;
            case F64Value number when !double.IsFinite(number.Value):
                throw InvalidLiteral("E-CONST-F64", "f64 constant must be finite");
            case SandboxPathValue path:
                EnsureTextLiteralLength(path.Value.RelativePath, "path");
                if (!SandboxLiteralConstraints.IsPortableRelativePath(path.Value.RelativePath))
                {
                    throw InvalidLiteral("E-CONST-PATH", "path constant must be a portable relative path");
                }

                return true;
            case SandboxUriValue uri:
                EnsureTextLiteralLength(uri.Value.Value, "uri");
                if (!IsSandboxUri(uri.Value.Value))
                {
                    throw InvalidLiteral("E-CONST-URI", "uri constant must be an absolute URI without user info");
                }

                return true;
            case ListValue or MapValue or RecordValue:
                return true;
            default:
                return false;
        }
    }

    private static IEnumerable<SandboxValue> Flatten(SandboxValue value)
    {
        var stack = new Stack<SandboxValue>();
        stack.Push(value);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            switch (current)
            {
                case ListValue list:
                    for (var i = list.Values.Count - 1; i >= 0; i--)
                    {
                        stack.Push(list.Values[i]);
                    }

                    break;
                case MapValue map:
                    foreach (var pair in map.Values)
                    {
                        stack.Push(pair.Value);
                        stack.Push(pair.Key);
                    }

                    break;
                case RecordValue record:
                    for (var i = record.Fields.Count - 1; i >= 0; i--)
                    {
                        stack.Push(record.Fields[i]);
                    }

                    break;
            }
        }
    }

    private static void EnsureTextLiteralLength(string value, string literalKind)
    {
        if (value.Length > MaxTextLiteralLength)
        {
            throw InvalidLiteral("E-CONST-HUGE", $"{literalKind} constant exceeds maximum length");
        }
    }

    private static bool IsSandboxUri(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.Contains('\\') &&
           Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           !string.IsNullOrWhiteSpace(uri.Host) &&
           string.IsNullOrEmpty(uri.UserInfo);

    private static SandboxValidationException InvalidLiteral(string code, string message)
        => new([new SandboxDiagnostic(code, message)]);
}
