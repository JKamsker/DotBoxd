using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox.Values;

internal static partial class SandboxValidatedValueShapeMeter
{
    private static bool TryMeasureScalar(
        SandboxValue value,
        SandboxType expectedType,
        ValidationFailure failure,
        ResourceLimits? limits,
        out ValueShape shape)
    {
        shape = new ValueShape(0, 0, 0, 0, 0, 0);
        switch (value)
        {
            case UnitValue:
                RequireScalarType(expectedType, "Unit", failure);
                return true;
            case BoolValue:
                RequireScalarType(expectedType, "Bool", failure);
                return true;
            case I32Value:
                RequireScalarType(expectedType, "I32", failure);
                return true;
            case I64Value:
                RequireScalarType(expectedType, "I64", failure);
                return true;
            case F64Value:
                RequireScalarType(expectedType, "F64", failure);
                RequireScalarInvariants(value, failure);
                return true;
            case StringValue text:
                RequireScalarType(expectedType, "String", failure);
                shape = AddText(shape, SandboxLiteralConstraints.TextShape(text.Value), limits);
                return true;
            case OpaqueIdValue id:
                RequireScalarType(expectedType, id.TypeName, failure);
                RequireOpaqueId(id, failure);
                shape = AddText(shape, SandboxLiteralConstraints.TextShape(id.Value), limits);
                return true;
            case SandboxPathValue path:
                RequireScalarType(expectedType, "SandboxPath", failure);
                RequireScalarInvariants(value, failure);
                shape = AddText(shape, SandboxLiteralConstraints.TextShape(path.Value.RelativePath), limits);
                return true;
            case SandboxUriValue uri:
                RequireScalarType(expectedType, "SandboxUri", failure);
                RequireScalarInvariants(value, failure);
                shape = AddText(shape, SandboxLiteralConstraints.TextShape(uri.Value.Value), limits);
                return true;
            case ListValue or MapValue or RecordValue:
                return false;
            default:
                throw Error(failure);
        }
    }

    private static void RequireScalarType(
        SandboxType expectedType,
        string typeName,
        ValidationFailure failure)
    {
        if (expectedType.Arguments.Count != 0 ||
            !string.Equals(expectedType.Name, typeName, StringComparison.Ordinal) ||
            !expectedType.IsKnown())
        {
            throw Error(failure);
        }
    }

    private static void RequireOpaqueId(
        OpaqueIdValue id,
        ValidationFailure failure)
    {
        if (!SandboxType.IsKnownOpaqueId(id.TypeName) ||
            !SandboxLiteralConstraints.IsOpaqueId(id.Value))
        {
            throw Error(failure);
        }
    }

    private static void RequireScalarInvariants(
        SandboxValue value,
        ValidationFailure failure)
    {
        switch (value)
        {
            case F64Value number when !double.IsFinite(number.Value):
                throw Error(failure);
            case SandboxPathValue path:
                if (path.Value?.RelativePath is not { } relativePath ||
                    !SandboxLiteralConstraints.IsPortableRelativePath(relativePath))
                {
                    throw Error(failure);
                }

                break;
            case SandboxUriValue uri:
                if (uri.Value?.Value is not { } valueText ||
                    !SandboxLiteralConstraints.IsSandboxUri(valueText))
                {
                    throw Error(failure);
                }

                break;
        }
    }
}
