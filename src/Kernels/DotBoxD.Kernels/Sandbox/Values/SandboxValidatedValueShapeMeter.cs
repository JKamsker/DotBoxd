using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox.Values;

internal static partial class SandboxValidatedValueShapeMeter
{
    public static ValueShape MeasureBindingReturn(
        SandboxValue value,
        SandboxType expectedType,
        string bindingId,
        ResourceLimits? limits = null,
        CancellationToken cancellationToken = default,
        ResourceMeter? meter = null)
        => MeasureCore(
            value,
            expectedType,
            ValidationFailure.BindingReturn(bindingId),
            limits,
            cancellationToken,
            meter);

    public static ValueShape Measure(
        SandboxValue value,
        SandboxType expectedType,
        SandboxErrorCode errorCode,
        string message,
        ResourceLimits? limits = null,
        CancellationToken cancellationToken = default,
        ResourceMeter? meter = null)
        => MeasureCore(
            value,
            expectedType,
            ValidationFailure.Fixed(errorCode, message),
            limits,
            cancellationToken,
            meter);

    private static ValueShape MeasureCore(
        SandboxValue value,
        SandboxType expectedType,
        ValidationFailure failure,
        ResourceLimits? limits,
        CancellationToken cancellationToken,
        ResourceMeter? meter)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryMeasureScalar(value, expectedType, failure, limits, out var scalarShape))
        {
            return scalarShape;
        }

        if (!expectedType.IsKnown())
        {
            throw Error(failure);
        }

        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Frame>();
        var shape = new ValueShape(0, 0, 0, 0, 0, 0);
        var scanned = 0;
        stack.Push(new Frame(value, expectedType, Depth: 0, Exit: false));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++scanned % 64 == 0)
            {
                meter?.ChargeFuel(1);
                meter?.CheckDeadline();
            }

            var frame = stack.Pop();
            if (frame.Exit)
            {
                active.Remove(frame.Value);
                continue;
            }

            ValidateKnownType(frame.Value, frame.ExpectedType, failure);
            RequireScalarInvariants(frame.Value, failure);
            switch (frame.Value)
            {
                case StringValue text:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(text.Value), limits);
                    break;
                case OpaqueIdValue id:
                    RequireOpaqueId(id, failure);
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(id.Value), limits);
                    break;
                case SandboxPathValue path:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(path.Value.RelativePath), limits);
                    break;
                case SandboxUriValue uri:
                    shape = AddText(shape, SandboxLiteralConstraints.TextShape(uri.Value.Value), limits);
                    break;
                case ListValue list:
                    shape = AddList(shape, list, frame.ExpectedType, frame.Depth, active, stack, limits, failure);
                    break;
                case MapValue map:
                    shape = AddMap(shape, map, frame.ExpectedType, frame.Depth, active, stack, limits, failure);
                    break;
                case RecordValue record:
                    shape = AddRecord(shape, record, frame.ExpectedType, frame.Depth, active, stack, limits, failure);
                    break;
                case UnitValue or BoolValue or I32Value or I64Value or F64Value:
                    break;
            }
        }

        return shape;
    }
    private static void Enter(
        object value,
        HashSet<object> active,
        ValidationFailure failure)
    {
        if (!active.Add(value))
        {
            throw Error(failure);
        }
    }

    private static void ValidateKnownType(
        SandboxValue value,
        SandboxType expectedType,
        ValidationFailure failure)
    {
        if (!SandboxValueTypeMatcher.MatchesValidationFrame(value, expectedType))
        {
            throw Error(failure);
        }
    }

    private static long AddLong(long current, long amount, string quotaMessage)
    {
        try
        {
            return checked(current + amount);
        }
        catch (OverflowException)
        {
            throw Quota(quotaMessage);
        }
    }

    private static SandboxRuntimeException Quota(string message)
        => new(new SandboxError(SandboxErrorCode.QuotaExceeded, message));

    private static SandboxRuntimeException Error(ValidationFailure failure)
        => new(new SandboxError(failure.Code, failure.Message));

    private readonly record struct ValidationFailure(
        SandboxErrorCode Code,
        string? StaticMessage,
        string? BindingId)
    {
        public static ValidationFailure Fixed(SandboxErrorCode code, string message)
            => new(code, message, null);

        public static ValidationFailure BindingReturn(string bindingId)
            => new(SandboxErrorCode.BindingFailure, null, bindingId);

        public string Message
            => StaticMessage ?? $"binding '{BindingId}' returned an unexpected value type";
    }

    private readonly record struct Frame(SandboxValue Value, SandboxType ExpectedType, int Depth, bool Exit);
}
