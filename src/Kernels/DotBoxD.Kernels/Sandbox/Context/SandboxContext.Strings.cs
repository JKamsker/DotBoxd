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

    // A return-credit scope only affects accounting through ChargeBindingReturn -> TryConsume, which credits a
    // pre-charged string ONLY when the return value is itself a bare StringValue (TryConsume never recurses into
    // composites). So only String-returning bindings can ever have their pre-charged return string credited;
    // SandboxType.String is a singleton, so ReferenceEquals selects exactly those. For any other return type the
    // scope would be allocated but never consumed, so skipping it is behavior-preserving: a composite return such
    // as List<String> is charged once via ChargeValueShape both before and after this optimization (the old
    // unconditional scope never credited composites either). The host string-producing facades (file read,
    // budgeted string ops, HTTP read) pre-charge via ChargeString and declare String returns, so they still get a
    // scope and are not double-charged -- pinned by Precharged_string_binding_return_is_not_charged_twice.
    internal BindingReturnCreditTracker.Scope BeginBindingReturnCreditScope(SandboxType returnType)
        => ReferenceEquals(returnType, SandboxType.String) ? ReturnCredits.BeginScope() : default;

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
