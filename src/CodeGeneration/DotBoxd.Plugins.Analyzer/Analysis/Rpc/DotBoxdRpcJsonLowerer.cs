namespace DotBoxd.Plugins.Analyzer;

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Lowers a <c>[KernelRpcService]</c> batch method body to DotBoxd.Kernels JSON IR (statements + expressions),
/// the same JSON the host imports at install. Supports the canonical batch shape: local declarations, a
/// <c>foreach</c> over a list, <c>if</c>/<c>else</c>, host-binding calls via <c>ctx.Host&lt;T&gt;()</c>,
/// building DTOs (<c>new T(...)</c>/<c>new T{...}</c> → <c>record.new</c>) and accumulating into a list
/// (<c>list.Add</c> → <c>list.add</c>), and <c>return</c>. Capabilities/effects from host bindings are
/// collected. Unsupported shapes throw <see cref="NotSupportedException"/> so the kernel fails safe. The
/// expression half lives in the partial <c>DotBoxdRpcJsonLowerer.Expressions.cs</c>.
/// </summary>
internal sealed partial class DotBoxdRpcJsonLowerer
{
    private readonly SemanticModel _model;
    private readonly ICollection<string> _capabilities;
    private readonly ICollection<string> _effects;
    private readonly CancellationToken _cancellationToken;
    private int _tempCounter;

    /// <summary>True once the body builds a list or record, so the manifest declares the Alloc effect.</summary>
    public bool Allocates { get; private set; }

    public DotBoxdRpcJsonLowerer(
        SemanticModel model,
        ICollection<string> capabilities,
        ICollection<string> effects,
        CancellationToken cancellationToken)
    {
        _model = model;
        _capabilities = capabilities;
        _effects = effects;
        _cancellationToken = cancellationToken;
    }

    public string LowerBody(BlockSyntax block) => LowerStatements(block.Statements);

    private string LowerStatements(IEnumerable<StatementSyntax> statements)
    {
        var parts = new List<string>();
        foreach (var statement in statements)
        {
            LowerStatement(statement, parts);
        }

        return "[" + string.Join(",", parts) + "]";
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
                output.Add(LowerExpressionStatement(expression.Expression));
                break;
            case ForEachStatementSyntax loop:
                LowerForEach(loop, output);
                break;
            case IfStatementSyntax branch:
                output.Add(LowerIf(branch));
                break;
            case ReturnStatementSyntax { Expression: { } returned }:
                output.Add(Obj(("op", Str("return")), ("value", LowerExpression(returned))));
                break;
            case BlockSyntax block:
                foreach (var inner in block.Statements)
                {
                    LowerStatement(inner, output);
                }

                break;
            default:
                throw new NotSupportedException($"Kernel RPC service statement '{statement.Kind()}' is not supported.");
        }
    }

    private void LowerLocalDeclaration(LocalDeclarationStatementSyntax local, List<string> output)
    {
        foreach (var declarator in local.Declaration.Variables)
        {
            if (declarator.Initializer is not { } initializer)
            {
                throw new NotSupportedException("Kernel RPC service locals must be initialized.");
            }

            output.Add(SetStatement(declarator.Identifier.ValueText, LowerExpression(initializer.Value)));
        }
    }

    private string LowerExpressionStatement(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } assignment:
                var value = assignment.Kind() == SyntaxKind.SimpleAssignmentExpression
                    ? LowerExpression(assignment.Right)
                    : LowerCompound(assignment, target);
                return SetStatement(target.Identifier.ValueText, value);
            case PostfixUnaryExpressionSyntax { Operand: IdentifierNameSyntax inc } postfix
                when postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression:
                var op = postfix.Kind() == SyntaxKind.PostIncrementExpression ? "add" : "sub";
                return SetStatement(inc.Identifier.ValueText, BinaryJson(op, Var(inc.Identifier.ValueText), I32(1)));
            case InvocationExpressionSyntax invocation when TryLowerListAdd(invocation) is { } listAdd:
                return listAdd;
            default:
                throw new NotSupportedException($"Kernel RPC service statement expression '{expression}' is not supported.");
        }
    }

    private string LowerCompound(AssignmentExpressionSyntax assignment, IdentifierNameSyntax target)
    {
        var op = assignment.Kind() switch
        {
            SyntaxKind.AddAssignmentExpression => "add",
            SyntaxKind.SubtractAssignmentExpression => "sub",
            SyntaxKind.MultiplyAssignmentExpression => "mul",
            SyntaxKind.DivideAssignmentExpression => "div",
            SyntaxKind.ModuloAssignmentExpression => "rem",
            _ => throw new NotSupportedException($"Kernel RPC service assignment '{assignment.Kind()}' is not supported.")
        };
        return BinaryJson(op, Var(target.Identifier.ValueText), LowerExpression(assignment.Right));
    }

    private string? TryLowerListAdd(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Add" } member ||
            member.Expression is not IdentifierNameSyntax list ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            DotBoxdRpcTypeMapper.ListElementType(TypeOf(member.Expression)) is null)
        {
            return null;
        }

        var item = LowerExpression(invocation.ArgumentList.Arguments[0].Expression);
        var listName = list.Identifier.ValueText;
        Allocates = true;
        return SetStatement(listName, Call("list.add", null, Var(listName), item));
    }

    private void LowerForEach(ForEachStatementSyntax loop, List<string> output)
    {
        var source = "__sir_src" + _tempCounter;
        var index = "__sir_i" + _tempCounter;
        _tempCounter++;
        output.Add(SetStatement(source, LowerExpression(loop.Expression)));

        var body = new List<string>
        {
            SetStatement(loop.Identifier.ValueText, Call("list.get", null, Var(source), Var(index)))
        };
        LowerStatement(loop.Statement, body);

        output.Add(Obj(
            ("op", Str("forRange")),
            ("local", Str(index)),
            ("start", I32(0)),
            ("end", Call("list.count", null, Var(source))),
            ("body", "[" + string.Join(",", body) + "]")));
    }

    private string LowerIf(IfStatementSyntax branch)
    {
        var then = new List<string>();
        LowerStatement(branch.Statement, then);
        var @else = new List<string>();
        if (branch.Else is { } elseClause)
        {
            LowerStatement(elseClause.Statement, @else);
        }

        return Obj(
            ("op", Str("if")),
            ("condition", LowerExpression(branch.Condition)),
            ("then", "[" + string.Join(",", then) + "]"),
            ("else", "[" + string.Join(",", @else) + "]"));
    }
}
