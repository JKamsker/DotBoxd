namespace DotBoxd.Plugins.Analyzer;

internal sealed record DotBoxdExpressionModel(string Source, string Type, bool Allocates);

internal sealed record DotBoxdStatementBodyModel(string Source, bool Allocates);

internal sealed record DotBoxdHandleModel(DotBoxdExpressionModel Target, DotBoxdExpressionModel Message)
{
    public bool Allocates => Target.Allocates || Message.Allocates;
}
