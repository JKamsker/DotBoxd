namespace DotBoxd.Kernels.Validation.Internal;

using DotBoxd.Kernels;

internal static class LiteralExpressionAnalyzer
{
    private const int MaxTextLiteralLength = 65_536;

    public static SandboxType Analyze(LiteralExpression literal, ref SandboxEffect effects)
    {
        var allocates = LiteralValueSafety.Validate(literal.Value);
        ValidateLiteralValue(literal.Value);
        if (allocates)
        {
            effects |= SandboxEffect.Alloc;
        }

        return literal.Value.Type;
    }

    private static void ValidateLiteralValue(SandboxValue value)
    {
        try
        {
            SandboxValueValidator.RequireType(
                value,
                value.Type,
                SandboxErrorCode.ValidationError,
                "literal constant is invalid");
        }
        catch (SandboxRuntimeException)
        {
            throw InvalidLiteral("E-CONST-VALUE", "literal constant is invalid");
        }
    }

    private static SandboxValidationException InvalidLiteral(string code, string message)
        => new([new SandboxDiagnostic(code, message)]);
}
