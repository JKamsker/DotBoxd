using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox;

public sealed partial class SandboxContext
{
    public void ChargeString(string value)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeString(value);
        ReturnCredits.RecordString(value);
    }

    public void ChargeStringAllocation(int charLength)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Budget.ChargeStringAllocation(charLength);
    }

    public string CreateChargedStringConcat(string left, string right)
    {
        var length = CheckedCharLength(left.Length, right.Length);
        ChargeStringAllocation(length);
        var text = string.Concat(left, right);
        ReturnCredits.RecordString(text);
        return text;
    }

    public string CreateChargedSubstring(string value, int startIndex, int length)
    {
        if (startIndex < 0 || length < 0 || startIndex > value.Length - length)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.InvalidInput,
                "string substring range is invalid"));
        }

        ChargeStringAllocation(length);
        var text = value.Substring(startIndex, length);
        ReturnCredits.RecordString(text);
        return text;
    }

    internal void RecordStringReturnCredit(string value)
        => ReturnCredits.RecordString(value);

    public IDisposable BeginBindingReturnCreditScope() => ReturnCredits.BeginScope();

    internal IDisposable? BeginBindingReturnCreditScope(SandboxType returnType)
        => ReferenceEquals(returnType, SandboxType.String) ? ReturnCredits.BeginScope() : null;

    private BindingReturnCreditTracker ReturnCredits => _returnCredits ??= new();

    private static int CheckedCharLength(int left, int right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "string byte budget exhausted"));
        }
    }
}
