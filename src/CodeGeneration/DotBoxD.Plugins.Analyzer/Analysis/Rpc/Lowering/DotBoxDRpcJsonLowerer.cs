using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;
/// <summary>
/// Lowers a <c>[ServerExtension]</c> batch method body to DotBoxD.Kernels JSON IR (statements + expressions),
/// the same JSON the host imports at install. Supports the canonical batch shape: local declarations, a
/// <c>foreach</c> over a list, <c>if</c>/<c>else</c>, host-binding calls via <c>ctx.Host&lt;T&gt;()</c> or
/// constructor-injected service fields, building DTOs (<c>new T(...)</c>/<c>new T{...}</c> →
/// <c>record.new</c>) and accumulating into a list (<c>list.Add</c> → <c>list.add</c>),
/// <c>return</c>, and the loop-control statements <c>continue</c>/<c>break</c> (lowered to the
/// kernel IR's structured loop control). Capabilities/effects from host bindings are collected. Unsupported shapes throw
/// <see cref="NotSupportedException"/> so the kernel fails safe. The
/// expression half lives in the partial <c>DotBoxDRpcJsonLowerer.Expressions.cs</c>.
/// </summary>
internal sealed partial class DotBoxDRpcJsonLowerer
{
    private readonly SemanticModel _model;
    private readonly ICollection<string> _capabilities;
    private readonly ICollection<string> _effects;
    private readonly CancellationToken _cancellationToken;
    private readonly IReadOnlyDictionary<string, RpcInlinedBinding>? _inlinedBindings;
    private readonly IReadOnlyCollection<string>? _inlineStack;
    private readonly Func<string, string>? _reserveGeneratedName;
    private readonly string? _serverContextParameterName;
    private readonly ITypeSymbol? _serverContextType;
    private readonly Dictionary<string, string> _serviceHandleLocals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reservedNames = new(StringComparer.Ordinal);
    private Func<AssignmentExpressionSyntax, Func<ExpressionSyntax, string>, string?>? _assignmentOverride;
    private Func<ExpressionSyntax, string?>? _expressionOverride;
    private List<string>? _expressionPrelude;
    private IReadOnlyList<string> _returnRecordFields = [];
    private string? _returnRecordType;
    private int _tempCounter;

    /// <summary>True once the body builds a list or record, so the manifest declares the Alloc effect.</summary>
    public bool Allocates { get; private set; }

    public string LowerBody(BlockSyntax block) => LowerBody(block, [], [], returnRecordType: null, assignmentOverride: null);
    internal void AddServiceHandleLocal(string name, string handleIdJson)
        => _serviceHandleLocals[name] = handleIdJson;
    internal string LowerBody(
        BlockSyntax block,
        IReadOnlyList<(string Name, ExpressionSyntax Value)> leadingLocals,
        IReadOnlyList<string> returnRecordFields,
        string? returnRecordType,
        Func<AssignmentExpressionSyntax, Func<ExpressionSyntax, string>, string?>? assignmentOverride,
        Func<ExpressionSyntax, string?>? expressionOverride = null)
    {
        _assignmentOverride = assignmentOverride;
        _expressionOverride = null;
        _returnRecordFields = returnRecordFields;
        _returnRecordType = returnRecordType;
        try
        {
            ReserveUserNames(block);
            var parts = new List<string>();
            for (var i = 0; i < leadingLocals.Count; i++)
            {
                parts.Add(SetStatement(leadingLocals[i].Name, LowerExpressionWithPrelude(leadingLocals[i].Value, parts)));
            }

            _expressionOverride = expressionOverride;
            LowerStatements(block.Statements, parts);
            return "[" + string.Join(",", parts) + "]";
        }
        finally
        {
            _assignmentOverride = null;
            _expressionOverride = null;
            _returnRecordFields = [];
            _returnRecordType = null;
        }
    }
    internal static string SetGeneratedLocal(string name, string value) => SetStatement(name, value);
    private void LowerStatements(IEnumerable<StatementSyntax> statements, List<string> parts)
    {
        foreach (var statement in statements)
        {
            LowerStatement(statement, parts);
        }
    }

    private void LowerStatement(StatementSyntax statement, List<string> output)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        switch (statement)
        {
            case LocalDeclarationStatementSyntax local:
                LowerLocalDeclaration(local, output);
                break;
            case ExpressionStatementSyntax expression:
                LowerExpressionStatement(expression.Expression, output);
                break;
            case ForEachStatementSyntax loop:
                LowerForEach(loop, output);
                break;
            case IfStatementSyntax branch:
                LowerIf(branch, output);
                break;
            case ReturnStatementSyntax { Expression: { } returned }:
                output.Add(Obj(
                    ("op", Str("return")),
                    ("value", ReturnValue(LowerExpressionWithPrelude(returned, output)))));
                break;
            case ReturnStatementSyntax:
                output.Add(Obj(("op", Str("return")), ("value", Unit())));
                break;
            case ContinueStatementSyntax:
                output.Add(Obj(("op", Str("continue"))));
                break;
            case BreakStatementSyntax:
                output.Add(Obj(("op", Str("break"))));
                break;
            case BlockSyntax block:
                foreach (var inner in block.Statements)
                {
                    LowerStatement(inner, output);
                }

                break;
            default:
                throw new NotSupportedException($"Server extension statement '{statement.Kind()}' is not supported.");
        }
    }

    private void LowerLocalDeclaration(LocalDeclarationStatementSyntax local, List<string> output)
    {
        foreach (var declarator in local.Declaration.Variables)
        {
            if (declarator.Initializer is not { } initializer)
            {
                throw new NotSupportedException("Server extension locals must be initialized.");
            }

            var localName = declarator.Identifier.ValueText;
            if (TryLowerServiceHandleLocal(localName, initializer.Value, output))
            {
                continue;
            }

            output.Add(SetStatement(localName, LowerExpressionWithPrelude(initializer.Value, output)));
        }
    }

    private void LowerExpressionStatement(ExpressionSyntax expression, List<string> output)
    {
        switch (expression)
        {
            case AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment:
                var value = assignment.Kind() == SyntaxKind.SimpleAssignmentExpression
                    ? LowerExpressionWithPrelude(assignment.Right, output)
                    : LowerCompound(assignment, target, output);
                output.Add(SetStatement(target.Identifier.ValueText, value));
                return;
            case AssignmentExpressionSyntax { Left: ElementAccessExpressionSyntax element } assignment
                when assignment.Kind() == SyntaxKind.SimpleAssignmentExpression &&
                     TryLowerMapIndexSet(element, assignment.Right, output) is { } mapSet:
                output.Add(mapSet);
                return;
            case AssignmentExpressionSyntax assignment
                when _assignmentOverride?.Invoke(assignment, expression => LowerExpressionWithPrelude(expression, output)) is { } lowered:
                output.Add(lowered);
                return;
            case PostfixUnaryExpressionSyntax { Operand: IdentifierNameSyntax inc } postfix
                when postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression:
                var op = postfix.Kind() == SyntaxKind.PostIncrementExpression ? "add" : "sub";
                output.Add(SetStatement(inc.Identifier.ValueText, BinaryJson(op, Var(inc.Identifier.ValueText), I32(1))));
                return;
            case InvocationExpressionSyntax invocation when TryLowerListAdd(invocation, output) is { } listAdd:
                output.Add(listAdd);
                return;
            case InvocationExpressionSyntax invocation:
                output.Add(SetStatement(
                    "__sir_discard" + _tempCounter++,
                    LowerExpressionWithPrelude(invocation, output)));
                return;
            case AwaitExpressionSyntax { Expression: InvocationExpressionSyntax invocation }:
                output.Add(SetStatement(
                    "__sir_discard" + _tempCounter++,
                    LowerExpressionWithPrelude(invocation, output)));
                return;
            default:
                throw new NotSupportedException($"Server extension statement expression '{expression}' is not supported.");
        }
    }

    private string LowerCompound(AssignmentExpressionSyntax assignment, IdentifierNameSyntax target, List<string> output)
    {
        var op = assignment.Kind() switch
        {
            SyntaxKind.AddAssignmentExpression => "add",
            SyntaxKind.SubtractAssignmentExpression => "sub",
            SyntaxKind.MultiplyAssignmentExpression => "mul",
            SyntaxKind.DivideAssignmentExpression => "div",
            SyntaxKind.ModuloAssignmentExpression => "rem",
            _ => throw new NotSupportedException($"Server extension assignment '{assignment.Kind()}' is not supported.")
        };
        return BinaryJson(op, Var(target.Identifier.ValueText), LowerExpressionWithPrelude(assignment.Right, output));
    }

    private string? TryLowerListAdd(InvocationExpressionSyntax invocation, List<string> output)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add" } member ||
            member.Expression is not IdentifierNameSyntax list ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            DotBoxDRpcTypeMapper.ListElementType(TypeOf(member.Expression)) is null)
        {
            return null;
        }

        var item = LowerExpressionWithPrelude(invocation.ArgumentList.Arguments[0].Expression, output);
        var listName = list.Identifier.ValueText;
        Allocates = true;
        return SetStatement(listName, Call("list.add", null, Var(listName), item));
    }

    private void LowerForEach(ForEachStatementSyntax loop, List<string> output)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(TypeOf(loop.Expression)) is not { } elementType)
        {
            throw new NotSupportedException(
                $"Server extension foreach source '{loop.Expression}' must be a supported list type.");
        }

        var suffix = NextLoopTempSuffix();
        var source = "__sir_src" + suffix;
        var index = "__sir_i" + suffix;
        output.Add(SetStatement(source, LowerExpressionWithPrelude(loop.Expression, output)));

        var body = new List<string>
        {
            SetStatement(loop.Identifier.ValueText, BuildForEachItem(loop, elementType, source, index))
        };
        LowerStatement(loop.Statement, body);

        output.Add(Obj(
            ("op", Str("forRange")),
            ("local", Str(index)),
            ("start", I32(0)),
            ("end", Call("list.count", null, Var(source))),
            ("body", "[" + string.Join(",", body) + "]")));
    }

    private string BuildForEachItem(
        ForEachStatementSyntax loop,
        ITypeSymbol elementType,
        string source,
        string index)
    {
        var local = _model.GetDeclaredSymbol(loop, _cancellationToken)
            ?? throw new NotSupportedException(
                $"Server extension foreach local '{loop.Identifier.ValueText}' could not be resolved.");
        var item = Call("list.get", null, Var(source), Var(index));
        return ApplyNumericConversion(elementType, local.Type, item);
    }

    private void LowerIf(IfStatementSyntax branch, List<string> output)
    {
        var then = new List<string>();
        LowerStatement(branch.Statement, then);
        var @else = new List<string>();
        if (branch.Else is { } elseClause)
        {
            LowerStatement(elseClause.Statement, @else);
        }

        output.Add(Obj(
            ("op", Str("if")),
            ("condition", LowerExpressionWithPrelude(branch.Condition, output)),
            ("then", "[" + string.Join(",", then) + "]"),
            ("else", "[" + string.Join(",", @else) + "]")));
    }

    private string ReturnValue(string userReturn)
    {
        if (_returnRecordFields.Count == 0)
        {
            return userReturn;
        }

        var fields = new string[1 + _returnRecordFields.Count];
        fields[0] = userReturn;
        for (var i = 0; i < _returnRecordFields.Count; i++)
        {
            fields[i + 1] = Var(_returnRecordFields[i]);
        }

        return Call("record.new", _returnRecordType, fields);
    }
}
