using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDHandleBodyModelFactory
{
    public static DotBoxDStatementBodyModel FromSend(DotBoxDHandleModel handle)
    {
        var returned = DotBoxDStatementBodyModelFactory.Return(
            SendExpression(handle),
            handle.Target.Allocates || handle.Message.Allocates);
        return handle.Prefix is null
            ? returned
            : DotBoxDStatementBodyModelFactory.Concat(handle.Prefix, returned);
    }

    public static DotBoxDStatementBodyModel ReturnUnit()
        => DotBoxDStatementBodyModelFactory.Return(
            $"new {TypeNames.GlobalLiteralExpression}({TypeNames.GlobalSandboxValue}.Unit, Span)",
            allocates: false);

    public static DotBoxDStatementBodyModel ReturnExpression(
        DotBoxDExpressionModel expression,
        DotBoxDStatementBodyModel? prefix = null)
    {
        var returned = DotBoxDStatementBodyModelFactory.Return(expression.Source, expression.Allocates);
        return prefix is null
            ? returned
            : DotBoxDStatementBodyModelFactory.Concat(prefix, returned);
    }

    private static string SendExpression(DotBoxDHandleModel handle)
        => $"new {TypeNames.GlobalCallExpression}({TypeNames.GlobalPluginMessageBindings}.SendBindingId, [{handle.Target.Source}, {handle.Message.Source}], null, Span)";
}
