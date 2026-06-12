namespace SafeIR.Validation;

using SafeIR;

internal static class DangerousReferenceDetector
{
    public static bool IsDangerousReference(string value)
        => SandboxDescriptorGuards.ContainsForbiddenDescriptor(value);

    public static void Scan(Statement statement, List<SandboxDiagnostic> diagnostics)
    {
        switch (statement)
        {
            case AssignmentStatement assignment:
                Check(assignment.Name, diagnostics, assignment.Span);
                Scan(assignment.Value, diagnostics);
                break;
            case ReturnStatement ret:
                Scan(ret.Value, diagnostics);
                break;
            case ExpressionStatement expr:
                Scan(expr.Value, diagnostics);
                break;
            case IfStatement branch:
                Scan(branch.Condition, diagnostics);
                ScanStatements(branch.Then, diagnostics);
                ScanStatements(branch.Else, diagnostics);
                break;
            case WhileStatement loop:
                Scan(loop.Condition, diagnostics);
                ScanStatements(loop.Body, diagnostics);
                break;
            case ForRangeStatement range:
                Check(range.LocalName, diagnostics, range.Span);
                Scan(range.Start, diagnostics);
                Scan(range.End, diagnostics);
                ScanStatements(range.Body, diagnostics);
                break;
        }
    }

    private static void Scan(Expression expression, List<SandboxDiagnostic> diagnostics)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                CheckLiteral(literal, diagnostics);
                break;
            case VariableExpression variable:
                Check(variable.Name, diagnostics, variable.Span);
                break;
            case UnaryExpression unary:
                Scan(unary.Operand, diagnostics);
                break;
            case BinaryExpression binary:
                Scan(binary.Left, diagnostics);
                Scan(binary.Right, diagnostics);
                break;
            case CallExpression call:
                Check(call.Name, diagnostics, call.Span);
                ScanExpressions(call.Arguments, diagnostics);
                break;
        }
    }

    private static void ScanStatements(IReadOnlyList<Statement> statements, List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            Scan(statements[i], diagnostics);
        }
    }

    private static void ScanExpressions(IReadOnlyList<Expression> expressions, List<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < expressions.Count; i++)
        {
            Scan(expressions[i], diagnostics);
        }
    }

    private static void Check(string? value, List<SandboxDiagnostic> diagnostics, SourceSpan span)
    {
        if (string.IsNullOrWhiteSpace(value) || ContainsControlCharacter(value))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-IR-ID",
                "IR identifiers and call names must be non-empty and must not contain control characters",
                Span: span));
            return;
        }

        if (IsDangerousReference(value))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", $"forbidden CLR reference '{value}'", Span: span));
        }
    }

    private static bool ContainsControlCharacter(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static void CheckLiteral(LiteralExpression literal, List<SandboxDiagnostic> diagnostics)
    {
        var text = literal.Value switch
        {
            StringValue value => value.Value,
            OpaqueIdValue value => value.Value,
            SandboxPathValue value => value.Value.RelativePath,
            SandboxUriValue value => value.Value.Value,
            _ => null
        };
        if (text is not null && IsDangerousReference(text))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", "forbidden CLR reference in literal", Span: literal.Span));
        }
    }
}
