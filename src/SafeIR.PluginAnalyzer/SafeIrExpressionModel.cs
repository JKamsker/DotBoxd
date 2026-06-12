namespace SafeIR.PluginAnalyzer;

internal sealed record SafeIrExpressionModel(string Source, string Type, bool Allocates);

internal sealed record SafeIrStatementBodyModel(string Source, bool Allocates);

internal sealed record SafeIrHandleModel(SafeIrExpressionModel Target, SafeIrExpressionModel Message)
{
    public bool Allocates => Target.Allocates || Message.Allocates;
}
