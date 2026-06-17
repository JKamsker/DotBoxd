using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDStatementBodyModelFactory
{
    public static DotBoxDExpressionModel Variable(string name, string type)
        => new($"{DotBoxDGenerationNames.Helpers.Var}({LiteralReader.StringLiteral(name)})", type, false);

    public static DotBoxDStatementBodyModel Assign(string name, DotBoxDExpressionModel value)
    {
        var source =
            $"new {StatementArrayType()} {{ new {TypeNames.GlobalAssignmentStatement}(" +
            $"{LiteralReader.StringLiteral(name)}, {value.Source}, Span) }}";
        return new DotBoxDStatementBodyModel(source, value.Allocates);
    }

    public static DotBoxDStatementBodyModel Return(string expression, bool allocates)
        => new(
            $"new {StatementArrayType()} {{ new {TypeNames.GlobalReturnStatement}({expression}, Span) }}",
            allocates);

    public static DotBoxDStatementBodyModel Concat(
        DotBoxDStatementBodyModel first,
        DotBoxDStatementBodyModel second)
        => new(
            TypeNames.GlobalEnumerable + ".ToArray(" +
            TypeNames.GlobalEnumerable + ".Concat<" + TypeNames.GlobalStatement + ">(" +
            $"{first.Source}, {second.Source}))",
            first.Allocates || second.Allocates);

    private static string StatementArrayType() => TypeNames.GlobalStatement + "[]";
}
