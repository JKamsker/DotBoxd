using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcKernelClientParameters
{
    public static int PayloadParameterCount(IMethodSymbol method)
        => method.Parameters.Length - (TryGetCancellationToken(method, out _) ? 1 : 0);

    public static string CancellationTokenArgument(IMethodSymbol method)
        => TryGetCancellationToken(method, out var parameter)
            ? RpcKernelClientParameterSource.Identifier(parameter.Name)
            : "default";

    public static bool TryGetCancellationToken(
        IMethodSymbol method,
        out IParameterSymbol parameter)
    {
        if (method.Parameters.Length > 0)
        {
            var candidate = method.Parameters[method.Parameters.Length - 1];
            if (IsCancellationToken(candidate.Type))
            {
                parameter = candidate;
                return true;
            }
        }

        parameter = null!;
        return false;
    }

    private static bool IsCancellationToken(ITypeSymbol type)
        => string.Equals(
            type.ToDisplayString(),
            "System.Threading.CancellationToken",
            StringComparison.Ordinal);
}
