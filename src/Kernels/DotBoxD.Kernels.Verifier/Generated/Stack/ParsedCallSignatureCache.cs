namespace DotBoxD.Kernels.Verifier.Generated;

internal sealed class ParsedCallSignatureCache
{
    private static readonly ParsedCallSignature Unknown =
        new(GeneratedStackTypeOperations.UnknownType, Array.Empty<string>());

    private readonly Dictionary<string, ParsedCallSignature> _signatures = new(StringComparer.Ordinal);

    public ParsedCallSignature Get(string? signature)
    {
        if (signature is null)
        {
            return Unknown;
        }

        if (_signatures.TryGetValue(signature, out var parsed))
        {
            return parsed;
        }

        parsed = Parse(signature);
        _signatures.Add(signature, parsed);
        return parsed;
    }

    private static ParsedCallSignature Parse(string signature)
    {
        var start = signature.IndexOf('(');
        var end = signature.LastIndexOf("):", StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return Unknown;
        }

        var returnType = signature[(end + 2)..];
        var parameterText = signature[(start + 1)..end];
        var parameters = parameterText.Length == 0
            ? Array.Empty<string>()
            : parameterText.Split(',', StringSplitOptions.None);
        return new ParsedCallSignature(returnType, parameters);
    }
}

internal sealed record ParsedCallSignature(string ReturnType, IReadOnlyList<string> Parameters);
