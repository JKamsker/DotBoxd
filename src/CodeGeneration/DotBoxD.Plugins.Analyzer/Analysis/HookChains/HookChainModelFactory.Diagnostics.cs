using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class HookChainModelFactory
{
    private static HookChainCreateResult? NotLoweredDiagnostic(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        string? detail = null)
    {
        if (TryRemoteRunLocalLocation(invocation, model, cancellationToken, out var location))
        {
            return new HookChainCreateResult(null, new HookChainNotLoweredDiagnostic(location));
        }

        if (TryResultChainLocation(invocation, model, cancellationToken, out var resultLocation, out var isLocalTerminal))
        {
            return new HookChainCreateResult(
                null,
                new HookChainNotLoweredDiagnostic(
                    resultLocation,
                    HookChainNotLoweredKind.ResultChain,
                    LocalResultTerminal: isLocalTerminal));
        }

        if (TryRunChainLocation(invocation, model, cancellationToken, out var runLocation))
        {
            return new HookChainCreateResult(
                null,
                new HookChainNotLoweredDiagnostic(
                    runLocation,
                    HookChainNotLoweredKind.RunChain,
                    Detail: detail ?? string.Empty));
        }

        return null;
    }

    // True when the call site is a Register/RegisterLocal terminal on a known hook pipeline - the surface whose
    // native terminal throws when the generator does not intercept it.
    private static bool TryResultChainLocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginDiagnosticLocation location,
        out bool isLocalTerminal)
    {
        location = default;
        isLocalTerminal = false;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            (!string.Equals(terminalAccess.Name.Identifier.ValueText, RegisterMethod, StringComparison.Ordinal) &&
             !string.Equals(terminalAccess.Name.Identifier.ValueText, RegisterLocalMethod, StringComparison.Ordinal)))
        {
            return false;
        }

        var receiverKind = ReceiverKind(model, terminalAccess.Expression, cancellationToken);
        if (receiverKind is not (HookChainReceiverKind.Local or HookChainReceiverKind.Remote))
        {
            var stages = new List<HookChainStage>();
            var seed = WalkToSeed(terminalAccess.Expression, stages);
            if (seed is null ||
                GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken)?.Kind !=
                GeneratedRemoteHookChainKind.Hook)
            {
                return false;
            }
        }

        isLocalTerminal = string.Equals(terminalAccess.Name.Identifier.ValueText, RegisterLocalMethod, StringComparison.Ordinal);
        location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
        return true;
    }

    // True when the call site is a remote RunLocal terminal: RunLocal whose receiver's static type is one of the
    // remote hook/subscription stage/pipeline types. Those (and only those) throw NotSupportedException when the
    // generator does not intercept them, so a remote RunLocal that produced no package will throw at runtime.
    private static bool TryRemoteRunLocalLocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginDiagnosticLocation location)
    {
        location = default;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            !string.Equals(terminalAccess.Name.Identifier.ValueText, RunLocalMethod, StringComparison.Ordinal))
        {
            return false;
        }

        if (ReceiverKind(model, terminalAccess.Expression, cancellationToken) == HookChainReceiverKind.Remote)
        {
            location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
            return true;
        }

        var stages = new List<HookChainStage>();
        var seed = WalkToSeed(terminalAccess.Expression, stages);
        if (seed is null ||
            GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken) is null)
        {
            return false;
        }

        location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
        return true;
    }

    private static bool TryRunChainLocation(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        CancellationToken cancellationToken,
        out PluginDiagnosticLocation location)
    {
        location = default;
        if (invocation.Expression is not MemberAccessExpressionSyntax terminalAccess ||
            !string.Equals(terminalAccess.Name.Identifier.ValueText, RunMethod, StringComparison.Ordinal))
        {
            return false;
        }

        if (ReceiverKind(model, terminalAccess.Expression, cancellationToken) is HookChainReceiverKind.Local
            or HookChainReceiverKind.Remote)
        {
            location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
            return true;
        }

        var stages = new List<HookChainStage>();
        var seed = WalkToSeed(terminalAccess.Expression, stages);
        if (seed is null ||
            GeneratedRemoteHookChainFallback.Candidate(seed, model, cancellationToken) is null)
        {
            return false;
        }

        location = PluginDiagnosticLocation.From(terminalAccess.Name.GetLocation());
        return true;
    }
}
