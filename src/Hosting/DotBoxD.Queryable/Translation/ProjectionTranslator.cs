using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

/// <summary>
/// Translates a projection body (<c>Expression&lt;Func&lt;TEvent, TProjection&gt;&gt;</c>) into the portable
/// <see cref="QueryProjection"/> AST. Supported shapes: the identity (<c>e =&gt; e</c>), a single dotted
/// member (<c>e =&gt; e.TargetId</c> or <c>e =&gt; e.Source.Id</c>), and DTO/anonymous construction from
/// member reads and constants (<c>e =&gt; new AttackNotice(e.AttackerId, e.TargetId, e.Damage)</c>).
/// </summary>
internal sealed class ProjectionTranslator(ParameterExpression parameter)
{
    private int _parameterIndex;

    /// <summary>Translates a projection body into a projection AST.</summary>
    public QueryProjection Translate(Expression body)
    {
        var expression = MemberPathReader.StripPathConvert(body, parameter);
        if (expression == parameter)
        {
            return QueryProjection.Identity;
        }

        if (MemberPathReader.TryReadPath(expression, parameter, out var path))
        {
            return QueryProjection.Member(path);
        }

        return expression switch
        {
            NewExpression construct => TranslateNew(construct),
            MemberInitExpression init => TranslateMemberInit(init),
            _ => throw QueryTranslationException.Unsupported(
                expression, "a projection must be the event, a member read, or a DTO/anonymous construction."),
        };
    }

    private QueryProjection TranslateNew(NewExpression construct)
    {
        var fields = new List<QueryProjectionField>(construct.Arguments.Count);
        AddConstructorFields(construct, fields);

        return QueryProjection.Construct(TypeName(construct.Type), fields);
    }

    private QueryProjection TranslateMemberInit(MemberInitExpression init)
    {
        var fields = new List<QueryProjectionField>(init.NewExpression.Arguments.Count + init.Bindings.Count);
        AddConstructorFields(init.NewExpression, fields);
        foreach (var binding in init.Bindings)
        {
            if (binding is not MemberAssignment assignment)
            {
                throw QueryTranslationException.Unsupported(
                    init, "only simple member assignments are supported in a projection initializer.");
            }

            fields.Add(BuildField(assignment.Member.Name, assignment.Expression));
        }

        return QueryProjection.Construct(TypeName(init.Type), fields);
    }

    private void AddConstructorFields(NewExpression construct, List<QueryProjectionField> fields)
    {
        var parameters = construct.Constructor?.GetParameters();
        for (var i = 0; i < construct.Arguments.Count; i++)
        {
            var name = construct.Members is { } members
                ? members[i].Name
                : parameters?[i].Name ?? $"item{i}";
            fields.Add(BuildField(name, construct.Arguments[i]));
        }
    }

    private QueryProjectionField BuildField(string name, Expression argument)
    {
        if (MemberPathReader.TryReadPath(argument, parameter, out var path))
        {
            return QueryProjectionField.FromMember(name, path);
        }

        if (QueryValueFactory.TryEvaluateObject(argument, parameter, out var raw))
        {
            var value = QueryValueFactory.ToValue(raw, argument) with { ParameterKey = "p" + _parameterIndex++ };
            return QueryProjectionField.FromConstant(name, value);
        }

        throw QueryTranslationException.Unsupported(
            argument, "a projection member must be an event member read or a constant.");
    }

    private static string TypeName(Type type) => type.FullName ?? type.Name;
}
