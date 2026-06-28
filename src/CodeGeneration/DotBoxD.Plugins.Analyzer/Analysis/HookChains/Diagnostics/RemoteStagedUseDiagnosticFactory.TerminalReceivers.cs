using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class RemoteStagedUseDiagnosticFactory
{
    private static bool TryTerminalReceiver(
        InvocationExpressionSyntax invocation,
        out string terminalName,
        out ExpressionSyntax receiver)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax access)
        {
            terminalName = access.Name.Identifier.ValueText;
            receiver = access.Expression;
            return true;
        }

        if (invocation.Expression is MemberBindingExpressionSyntax binding &&
            invocation.Parent is ConditionalAccessExpressionSyntax conditional &&
            conditional.WhenNotNull == invocation)
        {
            terminalName = binding.Name.Identifier.ValueText;
            receiver = conditional.Expression;
            return true;
        }

        terminalName = string.Empty;
        receiver = invocation;
        return false;
    }
}
