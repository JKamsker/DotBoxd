using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private static IReadOnlyList<DotBoxDExpressionModel> LowerHostBindingArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<IParameterSymbol> parameters,
        string bindingId,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var bound = BindHostBindingArguments(arguments, parameters, bindingId);
        var lowered = new DotBoxDExpressionModel[parameters.Count];

        foreach (var argument in bound.Assigned)
        {
            var parameter = parameters[argument.ParameterIndex];
            var expected = HostBindingManifestTag(parameter.Type, bindingId, $"argument {argument.ParameterIndex}");
            var value = lowerExpression(argument.Expression);
            if (!string.Equals(value.Type, expected, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Host binding '{bindingId}' argument {argument.ParameterIndex} must lower to {expected}.");
            }

            lowered[argument.ParameterIndex] = value;
        }

        for (var i = 0; i < lowered.Length; i++)
        {
            lowered[i] ??= LowerDefaultArgument(parameters[i], bindingId, i);
        }

        return lowered;
    }

    private static bool CanBindHostBindingArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<IParameterSymbol> parameters)
        => TryBindHostBindingArguments(arguments, parameters, bindingId: null, out _);

    private static BoundHostBindingArguments BindHostBindingArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<IParameterSymbol> parameters,
        string bindingId)
    {
        if (TryBindHostBindingArguments(arguments, parameters, bindingId, out var bound))
        {
            return bound;
        }

        throw new NotSupportedException($"Host binding '{bindingId}' call has unsupported arguments.");
    }

    private static bool TryBindHostBindingArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IReadOnlyList<IParameterSymbol> parameters,
        string? bindingId,
        out BoundHostBindingArguments bound)
    {
        bound = new BoundHostBindingArguments([]);
        if (arguments.Count > parameters.Count)
        {
            return Fail(bindingId, $" call must pass at most {parameters.Count} argument(s).");
        }

        var assigned = new bool[parameters.Count];
        var current = new List<(int ParameterIndex, ExpressionSyntax Expression)>(arguments.Count);
        var nextPositional = 0;
        var previousIndex = -1;
        for (var ordinal = 0; ordinal < arguments.Count; ordinal++)
        {
            var argument = arguments[ordinal];
            if (!argument.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                return Fail(bindingId, " arguments must be value arguments.");
            }

            var index = argument.NameColon is { } name
                ? IndexOfParameter(parameters, name.Name.Identifier.ValueText, bindingId)
                : nextPositional;
            if (index < 0)
            {
                return false;
            }

            if (index < previousIndex)
            {
                return Fail(bindingId, " named arguments must be written in parameter order.");
            }

            if (index >= parameters.Count || assigned[index])
            {
                return Fail(bindingId, " call has duplicate or misplaced arguments.");
            }

            if (parameters[index].RefKind != RefKind.None)
            {
                return Fail(bindingId, " parameters must be value parameters.");
            }

            current.Add((index, argument.Expression));
            assigned[index] = true;
            previousIndex = index;
            nextPositional = NextUnassigned(assigned, nextPositional);
        }

        for (var i = 0; i < assigned.Length; i++)
        {
            if (!assigned[i] && !parameters[i].HasExplicitDefaultValue)
            {
                return Fail(bindingId, $" call must pass parameter '{parameters[i].Name}'.");
            }
        }

        bound = new BoundHostBindingArguments(current);
        return true;
    }

    private static DotBoxDExpressionModel LowerDefaultArgument(
        IParameterSymbol parameter,
        string bindingId,
        int index)
    {
        var expected = HostBindingManifestTag(parameter.Type, bindingId, $"argument {index}");
        if (parameter.ExplicitDefaultValue is null && parameter.Type.IsReferenceType)
        {
            throw new NotSupportedException(
                $"Host binding '{bindingId}' argument {index} default cannot be null.");
        }

        return LowerLiteralDefault(parameter.Type, parameter.ExplicitDefaultValue, expected, bindingId, index);
    }

    private static DotBoxDExpressionModel LowerLiteralDefault(
        ITypeSymbol type,
        object? value,
        string expected,
        string bindingId,
        int index)
    {
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
                ? Int64Default(EnumConstantToInt64(value, enumType))
                : Int32Default(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        return expected switch
        {
            DotBoxDGenerationNames.ManifestTypes.Bool when value is null && type.IsValueType => BoolDefault(false),
            DotBoxDGenerationNames.ManifestTypes.Bool when value is bool boolean => BoolDefault(boolean),
            DotBoxDGenerationNames.ManifestTypes.Int when value is null && type.IsValueType => Int32Default(0),
            DotBoxDGenerationNames.ManifestTypes.Int when value is IConvertible number =>
                Int32Default(number.ToInt32(System.Globalization.CultureInfo.InvariantCulture)),
            DotBoxDGenerationNames.ManifestTypes.Long when value is null && type.IsValueType => Int64Default(0),
            DotBoxDGenerationNames.ManifestTypes.Long when value is IConvertible number =>
                Int64Default(number.ToInt64(System.Globalization.CultureInfo.InvariantCulture)),
            DotBoxDGenerationNames.ManifestTypes.Double when value is null && type.IsValueType => Float64Default(0),
            DotBoxDGenerationNames.ManifestTypes.Double when value is IConvertible number =>
                Float64Default(number.ToDouble(System.Globalization.CultureInfo.InvariantCulture)),
            DotBoxDGenerationNames.ManifestTypes.String when value is string text => StringDefault(text),
            _ => throw new NotSupportedException(
                $"Host binding '{bindingId}' argument {index} default must be a supported scalar literal.")
        };
    }

    private static DotBoxDExpressionModel BoolDefault(bool value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.Bool}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            false);

    private static DotBoxDExpressionModel Int32Default(int value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Int,
            false);

    private static DotBoxDExpressionModel Int64Default(long value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Long,
            false);

    private static DotBoxDExpressionModel Float64Default(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new NotSupportedException("Host binding default double values must be finite.");
        }

        return new DotBoxDExpressionModel(
            $"{DotBoxDGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Double,
            false);
    }

    private static DotBoxDExpressionModel StringDefault(string value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.String,
            true);

    private static long EnumConstantToInt64(object? value, INamedTypeSymbol enumType)
        => enumType.EnumUnderlyingType?.SpecialType == SpecialType.System_UInt64
            ? unchecked((long)Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture))
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

    private static bool Fail(string? bindingId, string message)
    {
        if (bindingId is null)
        {
            return false;
        }

        throw new NotSupportedException($"Host binding '{bindingId}'{message}");
    }

    private static int NextUnassigned(bool[] assigned, int start)
    {
        while (start < assigned.Length && assigned[start])
        {
            start++;
        }

        return start;
    }

    private static int IndexOfParameter(IReadOnlyList<IParameterSymbol> parameters, string name, string? bindingId)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        if (bindingId is null)
        {
            return -1;
        }

        throw new NotSupportedException($"Host binding '{bindingId}' has no parameter '{name}'.");
    }

    private readonly record struct BoundHostBindingArguments(
        IReadOnlyList<(int ParameterIndex, ExpressionSyntax Expression)> Assigned);
}
