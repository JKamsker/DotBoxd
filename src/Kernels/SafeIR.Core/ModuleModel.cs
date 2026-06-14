namespace SafeIR;

public sealed record CapabilityRequest(string Id, string? Reason);

public sealed record SandboxModule(
    string Id,
    SemVersion Version,
    SemVersion TargetSandboxVersion,
    IReadOnlyList<CapabilityRequest> CapabilityRequests,
    IReadOnlyList<SandboxFunction> Functions,
    IReadOnlyDictionary<string, string> Metadata)
{
    private IReadOnlyList<CapabilityRequest> _capabilityRequests = ModelCopy.List(CapabilityRequests);
    private IReadOnlyList<SandboxFunction> _functions = ModelCopy.List(Functions);
    private IReadOnlyDictionary<string, string> _metadata = ModelCopy.StringDictionary(Metadata);

    public IReadOnlyList<CapabilityRequest> CapabilityRequests { get => _capabilityRequests; init => _capabilityRequests = ModelCopy.List(value); }
    public IReadOnlyList<SandboxFunction> Functions { get => _functions; init => _functions = ModelCopy.List(value); }
    public IReadOnlyDictionary<string, string> Metadata { get => _metadata; init => _metadata = ModelCopy.StringDictionary(value); }
}

public sealed record SandboxFunction(
    string Id,
    bool IsEntrypoint,
    IReadOnlyList<Parameter> Parameters,
    SandboxType ReturnType,
    IReadOnlyList<Statement> Body,
    SandboxEffect? DeclaredEffects = null)
{
    private IReadOnlyList<Parameter> _parameters = ModelCopy.List(Parameters);
    private IReadOnlyList<Statement> _body = ModelCopy.List(Body);

    public IReadOnlyList<Parameter> Parameters { get => _parameters; init => _parameters = ModelCopy.List(value); }
    public IReadOnlyList<Statement> Body { get => _body; init => _body = ModelCopy.List(value); }
}

public sealed record Parameter(string Name, SandboxType Type);

public abstract record Statement(SourceSpan Span);

public sealed record AssignmentStatement(string Name, Expression Value, SourceSpan Span) : Statement(Span);

public sealed record ReturnStatement(Expression Value, SourceSpan Span) : Statement(Span);

public sealed record ExpressionStatement(Expression Value, SourceSpan Span) : Statement(Span);

public sealed record IfStatement(Expression Condition, IReadOnlyList<Statement> Then, IReadOnlyList<Statement> Else, SourceSpan Span)
    : Statement(Span)
{
    private IReadOnlyList<Statement> _then = ModelCopy.List(Then);
    private IReadOnlyList<Statement> _else = ModelCopy.List(Else);

    public IReadOnlyList<Statement> Then { get => _then; init => _then = ModelCopy.List(value); }
    public IReadOnlyList<Statement> Else { get => _else; init => _else = ModelCopy.List(value); }
}

public sealed record WhileStatement(Expression Condition, IReadOnlyList<Statement> Body, SourceSpan Span) : Statement(Span)
{
    private IReadOnlyList<Statement> _body = ModelCopy.List(Body);

    public IReadOnlyList<Statement> Body { get => _body; init => _body = ModelCopy.List(value); }
}

public sealed record ForRangeStatement(string LocalName, Expression Start, Expression End, IReadOnlyList<Statement> Body, SourceSpan Span)
    : Statement(Span)
{
    private IReadOnlyList<Statement> _body = ModelCopy.List(Body);

    public IReadOnlyList<Statement> Body { get => _body; init => _body = ModelCopy.List(value); }
}

public abstract record Expression(SourceSpan Span);

public sealed record LiteralExpression(SandboxValue Value, SourceSpan Span) : Expression(Span);

public sealed record VariableExpression(string Name, SourceSpan Span) : Expression(Span);

public sealed record UnaryExpression(string Operator, Expression Operand, SourceSpan Span) : Expression(Span);

public sealed record BinaryExpression(Expression Left, string Operator, Expression Right, SourceSpan Span) : Expression(Span);

public sealed record CallExpression(string Name, IReadOnlyList<Expression> Arguments, SandboxType? GenericType, SourceSpan Span)
    : Expression(Span)
{
    private IReadOnlyList<Expression> _arguments = ModelCopy.List(Arguments);

    public IReadOnlyList<Expression> Arguments { get => _arguments; init => _arguments = ModelCopy.List(value); }
}
