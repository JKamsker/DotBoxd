using System.Linq.Expressions;
using System.Reflection;
using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static List<MemberBinding>? RecordTailBindings(
        IReadOnlyList<int> constructorMap,
        IReadOnlyList<RecordMember> fields,
        Func<int, LinqExpression> fieldExpression,
        Func<LinqExpression, Type, LinqExpression> readField)
    {
        var assigned = new bool[fields.Count];
        for (var i = 0; i < constructorMap.Count; i++)
        {
            assigned[constructorMap[i]] = true;
        }

        var bindings = new List<MemberBinding>();
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned[i])
            {
                continue;
            }

            if (!CanBindInitializerMember(fields[i]))
            {
                return null;
            }

            bindings.Add(LinqExpression.Bind(
                fields[i].Member,
                LinqExpression.Convert(readField(fieldExpression(i), fields[i].Type), fields[i].Type)));
        }

        return bindings;
    }

    private static bool CanBindInitializerMember(RecordMember member)
        => member.Member switch
        {
            PropertyInfo property => property.SetMethod is { IsPublic: true },
            FieldInfo field => !field.IsInitOnly,
            _ => false,
        };
}
