using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDExpressionModelFactory
{
    public static DotBoxDExpressionModel Create(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context)
        => Lower(expression, context);

    private static DotBoxDExpressionModel Lower(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (DotBoxDConstantExpressionLowerer.TryLower(
                expression,
                context.SemanticModel,
                context.CancellationToken) is { } constant)
        {
            return constant;
        }

        return expression switch {
            ParenthesizedExpressionSyntax parenthesized => Lower(parenthesized.Expression, context),
            PrefixUnaryExpressionSyntax unary => LowerUnary(unary, context),
            BinaryExpressionSyntax binary => LowerBinary(binary, context),
            InvocationExpressionSyntax invocation =>
                DotBoxDInvocationExpressionLowerer.Lower(invocation, context, part => Lower(part, context)),
            IsPatternExpressionSyntax pattern => DotBoxDPatternExpressionLowerer.Lower(pattern, context, part => Lower(part, context)),
            IdentifierNameSyntax identifier => LowerIdentifier(identifier.Identifier.ValueText, context),
            MemberAccessExpressionSyntax member
                when DotBoxDStringExpressionLowerer.TryLowerMember(member, context, part => Lower(part, context)) is { } lowered =>
                lowered,
            MemberAccessExpressionSyntax member => LowerMemberAccess(member, context),
            InterpolatedStringExpressionSyntax interpolated =>
                DotBoxDInterpolatedStringExpressionLowerer.Lower(interpolated, part => Lower(part, context)),
            BaseObjectCreationExpressionSyntax creation
                when DotBoxDRecordCreationExpressionLowerer.TryLower(creation, context, part => Lower(part, context)) is { } record =>
                record,
            AnonymousObjectCreationExpressionSyntax anonymous
                when DotBoxDAnonymousObjectCreationExpressionLowerer.TryLower(anonymous, context, part => Lower(part, context)) is { } anonymousRecord =>
                anonymousRecord,
            LiteralExpressionSyntax literal => DotBoxDLiteralExpressionLowerer.Lower(literal),
            _ => Unsupported(expression)
        };
    }

    private static DotBoxDExpressionModel LowerUnary(
        PrefixUnaryExpressionSyntax unary,
        DotBoxDExpressionLoweringContext context)
    {
        if (DotBoxDLiteralExpressionLowerer.TryLowerNegative(unary) is { } literal)
        {
            return literal;
        }

        var operand = Lower(unary.Operand, context);
        return unary.Kind() switch {
            SyntaxKind.LogicalNotExpression => Unary(
                DotBoxDGenerationNames.Helpers.Not,
                DotBoxDGenerationNames.Operators.LogicalNot,
                operand,
                DotBoxDGenerationNames.ManifestTypes.Bool,
                DotBoxDGenerationNames.ManifestTypes.Bool),
            SyntaxKind.UnaryMinusExpression => DotBoxDNumericExpressionLowerer.Unary(
                DotBoxDGenerationNames.Helpers.Neg,
                DotBoxDGenerationNames.Operators.Minus,
                operand),
            _ => Unsupported(unary)
        };
    }

    private static DotBoxDExpressionModel Unary(
        string helper,
        string symbol,
        DotBoxDExpressionModel operand,
        string expected,
        string resultType)
    {
        RequireType(operand, expected, $"Unary operator '{symbol}'");
        return new DotBoxDExpressionModel($"{helper}({operand.Source})", resultType, operand.Allocates);
    }

    private static DotBoxDExpressionModel LowerBinary(
        BinaryExpressionSyntax binary,
        DotBoxDExpressionLoweringContext context)
    {
        var left = Lower(binary.Left, context);
        var right = Lower(binary.Right, context);
        DotBoxDNumericConstantPromoter.Promote(binary, context, ref left, ref right);
        var allocates = left.Allocates || right.Allocates;

        return binary.Kind() switch {
            SyntaxKind.EqualsExpression => DotBoxDEqualityExpressionLowerer.Lower(
                left,
                right,
                negate: false,
                allocates),
            SyntaxKind.NotEqualsExpression => DotBoxDEqualityExpressionLowerer.Lower(
                left,
                right,
                negate: true,
                allocates),
            SyntaxKind.GreaterThanOrEqualExpression => NumericBinary(
                DotBoxDGenerationNames.Helpers.Ge,
                DotBoxDGenerationNames.Operators.GreaterThanOrEqual,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.GreaterThanExpression => NumericBinary(
                DotBoxDGenerationNames.Helpers.Gt,
                DotBoxDGenerationNames.Operators.GreaterThan,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.LessThanOrEqualExpression => NumericBinary(
                DotBoxDGenerationNames.Helpers.Le,
                DotBoxDGenerationNames.Operators.LessThanOrEqual,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.LessThanExpression => NumericBinary(
                DotBoxDGenerationNames.Helpers.Lt,
                DotBoxDGenerationNames.Operators.LessThan,
                left,
                right,
                comparison: true,
                allocates),
            SyntaxKind.LogicalAndExpression => BoolBinary(
                DotBoxDGenerationNames.Helpers.And,
                DotBoxDGenerationNames.Operators.LogicalAnd,
                left,
                right,
                allocates),
            SyntaxKind.LogicalOrExpression => BoolBinary(
                DotBoxDGenerationNames.Helpers.Or,
                DotBoxDGenerationNames.Operators.LogicalOr,
                left,
                right,
                allocates),
            SyntaxKind.AddExpression => AddBinary(left, right, allocates),
            SyntaxKind.SubtractExpression => NumericBinary(
                DotBoxDGenerationNames.Helpers.Sub,
                DotBoxDGenerationNames.Operators.Minus,
                left,
                right,
                comparison: false,
                allocates),
            SyntaxKind.MultiplyExpression => NumericBinary(
                DotBoxDGenerationNames.Helpers.Mul,
                DotBoxDGenerationNames.Operators.Multiply,
                left,
                right,
                comparison: false,
                allocates),
            SyntaxKind.DivideExpression => NumericBinary(
                DotBoxDGenerationNames.Helpers.Div,
                DotBoxDGenerationNames.Operators.Divide,
                left,
                right,
                comparison: false,
                allocates),
            SyntaxKind.ModuloExpression => NumericBinary(
                DotBoxDGenerationNames.Helpers.Mod,
                DotBoxDGenerationNames.Operators.Modulo,
                left,
                right,
                comparison: false,
                allocates),
            _ => Unsupported(binary)
        };
    }

    private static DotBoxDExpressionModel AddBinary(
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool allocates)
    {
        if (IsString(left) && IsString(right))
        {
            return new DotBoxDExpressionModel(
                $"{DotBoxDGenerationNames.Helpers.ConcatString}({left.Source}, {right.Source})",
                DotBoxDGenerationNames.ManifestTypes.String,
                true);
        }

        if (IsString(left) || IsString(right))
        {
            throw new NotSupportedException(
                "Operator '+' requires both operands to be strings or matching numeric operands.");
        }

        return NumericBinary(
            DotBoxDGenerationNames.Helpers.Add,
            DotBoxDGenerationNames.Operators.Add,
            left,
            right,
            comparison: false,
            allocates);
    }

    private static DotBoxDExpressionModel NumericBinary(
        string helper,
        string symbol,
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool comparison,
        bool allocates)
        => DotBoxDNumericExpressionLowerer.Binary(helper, symbol, left, right, comparison, allocates);

    private static DotBoxDExpressionModel BoolBinary(
        string helper,
        string symbol,
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool allocates)
    {
        RequireType(left, DotBoxDGenerationNames.ManifestTypes.Bool, $"Operator '{symbol}'");
        RequireType(right, DotBoxDGenerationNames.ManifestTypes.Bool, $"Operator '{symbol}'");
        return new DotBoxDExpressionModel(
            $"{helper}({left.Source}, {right.Source})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            allocates);
    }

    private static DotBoxDExpressionModel LowerIdentifier(
        string name,
        DotBoxDExpressionLoweringContext context)
    {
        // Inside an inlined [KernelMethod] body: a reference to one of the method's parameters resolves to
        // the already-lowered IR of the call-site argument. Checked first so a parameter shadows any
        // same-named live setting (correct C# scoping).
        if (context.InlinedBindings is { } bindings &&
            bindings.TryGetValue(name, out var bound))
        {
            return bound;
        }

        // A Select-projected element: inline its already-lowered IR wherever the downstream lambda
        // names it (compile-time substitution; the projection's event-property refs ride along).
        if (context.ProjectedElementName is { } projectedName &&
            string.Equals(projectedName, name, StringComparison.Ordinal))
        {
            return context.ProjectedElement!;
        }

        var liveSettings = context.LiveSettings;
        for (var i = 0; i < liveSettings.Count; i++) {
            var setting = liveSettings[i];
            if (string.Equals(setting.Name, name, StringComparison.Ordinal)) {
                return new DotBoxDExpressionModel(
                    $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(name)})",
                    setting.Type,
                    false);
            }
        }

        throw new NotSupportedException($"Unsupported plugin identifier '{name}'.");
    }

    private static DotBoxDExpressionModel LowerMemberAccess(
        MemberAccessExpressionSyntax member,
        DotBoxDExpressionLoweringContext context)
    {
        var memberName = member.Name.Identifier.ValueText;
        if (member.Expression is IdentifierNameSyntax identifier) {
            // After a Select, the downstream lambda's parameter is the PROJECTED element. A field access on it
            // (dto.X) reads the projection's field by name via record.get — checked BEFORE the event-property
            // branch so a field that shares a name with an event property is never silently misread as it. If it
            // is not a record field (e.g. .Count on a projected list), fall through to the general member chain.
            if (context.ProjectedElementName is { } projectedName &&
                string.Equals(identifier.Identifier.ValueText, projectedName, StringComparison.Ordinal)) {
                if (TryLowerProjectedRecordField(memberName, context) is { } projectedField) {
                    return projectedField;
                }
            }
            else if (string.Equals(identifier.Identifier.ValueText, context.EventParameterName, StringComparison.Ordinal)) {
                for (var i = 0; i < context.EventProperties.Count; i++) {
                    var property = context.EventProperties[i];
                    if (string.Equals(property.Name, memberName, StringComparison.Ordinal)) {
                        CollectEventPropertyCapability(member, context);
                        return new DotBoxDExpressionModel(
                            $"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(EventVariable(memberName))})",
                            property.Type,
                            false);
                    }
                }

                throw new NotSupportedException($"Unknown event property '{memberName}'.");
            }
        }

        if (member.Expression is ThisExpressionSyntax) {
            return LowerIdentifier(memberName, context);
        }

        // General member chain: a `.Count`/`.Length` read on a list-shaped receiver, or a field read on a record
        // receiver. The receiver may itself be a projected element, an event property, a host-call result, or a
        // further chain hop — it is lowered recursively and dispatched on its sandbox shape, so e.g.
        // `ctx...GetInRange(id, 4).Count` or `dto.Inner.Field` lower. Anything else fails safe.
        if (TryLowerMemberChain(member, memberName, context) is { } chained) {
            return chained;
        }

        return Unsupported(member);
    }

    // Reads a field of the projected record (after a Select) as record.get(projection, index). The field is
    // matched by name against the projected DTO's declared fields — the same positional order record.new emitted
    // and the kernel parameter / decoder use — so dto.Field crosses to exactly that field's value and type.
    // Returns null when the projection is not a record or has no such field, so the caller can try the general
    // member chain; it is never reinterpreted as an event property.
    private static DotBoxDExpressionModel? TryLowerProjectedRecordField(
        string memberName,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.ProjectedElement is { } projected &&
            context.ProjectedElementType is INamedTypeSymbol recordType &&
            IsRecordShaped(recordType))
        {
            var fields = DotBoxDRpcTypeMapper.RecordFields(recordType);
            for (var i = 0; i < fields.Count; i++)
            {
                if (!string.Equals(fields[i].Name, memberName, StringComparison.Ordinal))
                {
                    continue;
                }

                return RecordGet(projected, i, fields[i].Type, allocates: false);
            }
        }

        return null;
    }

    // The general member chain: lower the receiver expression, then read `.Count`/`.Length` off a list value
    // (list.count -> i32) or a named field off a record value (record.get -> field type). The receiver's CLR
    // type drives which read applies; the lowered receiver's sandbox shape is double-checked so a mismatch
    // fails safe rather than emitting an invalid intrinsic.
    private static DotBoxDExpressionModel? TryLowerMemberChain(
        MemberAccessExpressionSyntax member,
        string memberName,
        DotBoxDExpressionLoweringContext context)
    {
        var receiverType = ResolveType(member.Expression, context);
        if (receiverType is null)
        {
            return null;
        }

        if ((string.Equals(memberName, "Count", StringComparison.Ordinal) ||
             string.Equals(memberName, "Length", StringComparison.Ordinal)) &&
            IsListShaped(receiverType))
        {
            var receiver = Lower(member.Expression, context);
            if (!string.Equals(receiver.Type, DotBoxDGenerationNames.ManifestTypes.List, StringComparison.Ordinal))
            {
                return null;
            }

            var source =
                $"new {DotBoxDGenerationNames.TypeNames.GlobalCallExpression}(" +
                $"{LiteralReader.StringLiteral("list.count")}, [{receiver.Source}], null, Span)";
            return new DotBoxDExpressionModel(source, DotBoxDGenerationNames.ManifestTypes.Int, receiver.Allocates);
        }

        if (receiverType is INamedTypeSymbol named && IsRecordShaped(named))
        {
            var fields = DotBoxDRpcTypeMapper.RecordFields(named);
            for (var i = 0; i < fields.Count; i++)
            {
                if (!string.Equals(fields[i].Name, memberName, StringComparison.Ordinal))
                {
                    continue;
                }

                var receiver = Lower(member.Expression, context);
                if (!string.Equals(receiver.Type, DotBoxDGenerationNames.ManifestTypes.Record, StringComparison.Ordinal))
                {
                    return null;
                }

                return RecordGet(receiver, i, fields[i].Type, receiver.Allocates);
            }
        }

        return null;
    }

    // A type is "record-shaped" only when its marshaller manifest tag is Record — i.e. it is a wire-eligible DTO
    // and NOT a list/map/scalar. This mirrors SandboxTypeSourceEmitter's list-before-record dispatch and avoids
    // the trap where IsRecordDto alone is true for a List/collection (which exposes public Count/Capacity
    // properties) and a field read would be emitted as record.get on a List value.
    private static bool IsRecordShaped(ITypeSymbol type)
        => string.Equals(
            SandboxTypeSourceEmitter.ManifestTag(type),
            DotBoxDGenerationNames.ManifestTypes.Record,
            StringComparison.Ordinal);

    private static DotBoxDExpressionModel RecordGet(
        DotBoxDExpressionModel record,
        int index,
        ITypeSymbol fieldType,
        bool allocates)
    {
        var source =
            $"new {DotBoxDGenerationNames.TypeNames.GlobalCallExpression}(" +
            $"{LiteralReader.StringLiteral("record.get")}, " +
            $"[{record.Source}, {DotBoxDGenerationNames.Helpers.I32}({index})], null, Span)";
        return new DotBoxDExpressionModel(source, SandboxTypeSourceEmitter.ManifestTag(fieldType), allocates);
    }

    private static ITypeSymbol? ResolveType(ExpressionSyntax expression, DotBoxDExpressionLoweringContext context)
    {
        var info = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return info.Type ?? info.ConvertedType;
    }

    private static bool IsListShaped(ITypeSymbol type)
    {
        try
        {
            return DotBoxDRpcTypeMapper.ListElementType(type) is not null;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Records the capability gating a <c>[Capability]</c>-annotated event property so reading it
    /// contributes to the kernel's required capabilities (deny-at-install if the policy lacks it).
    /// Unannotated properties stay ungated.
    /// </summary>
    private static void CollectEventPropertyCapability(
        MemberAccessExpressionSyntax member,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.Capabilities is null ||
            context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol is not IPropertySymbol property)
        {
            return;
        }

        foreach (var attribute in property.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDGenerationNames.Metadata.CapabilityAttribute,
                    StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string id &&
                !string.IsNullOrEmpty(id))
            {
                context.Capabilities.Add(id);
            }
        }
    }

    public static string EventVariable(string name) => DotBoxDGenerationNames.GeneratedVariables.EventPrefix + name;

    private static void RequireType(DotBoxDExpressionModel expression, string expected, string context)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal)) {
            throw new NotSupportedException($"{context} requires {expected} operands.");
        }
    }

    internal static bool IsString(DotBoxDExpressionModel expression)
        => string.Equals(expression.Type, DotBoxDGenerationNames.ManifestTypes.String, StringComparison.Ordinal);

    private static DotBoxDExpressionModel Unsupported(ExpressionSyntax expression)
        => throw new NotSupportedException($"Unsupported plugin expression '{expression}'.");
}
