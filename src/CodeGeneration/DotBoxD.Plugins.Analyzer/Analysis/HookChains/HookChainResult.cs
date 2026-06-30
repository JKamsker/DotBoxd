using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// A lowered hook chain: the package model (emitted like a kernel) plus optional interception metadata
/// for the C# interceptor that replaces the <c>Run(lambda)</c> call site with
/// <c>UseGeneratedChain</c>. Interception is null when the call site has no interceptable location —
/// the package still generates.
/// </summary>
internal sealed record HookChainResult(PluginKernelModel Model, HookChainInterception? Interception);

/// <summary>
/// The outcome of one hook-chain call site: either a lowered <see cref="HookChainResult"/>, or — for a recognized
/// recognized chain that could not be lowered — a <see cref="HookChainNotLoweredDiagnostic"/> the generator
/// reports so the otherwise-silent skip surfaces at build time. A null create result means the call site is not a
/// recognized chain at all (nothing to report).
/// </summary>
internal sealed record HookChainCreateResult(
    HookChainResult? Chain,
    HookChainNotLoweredDiagnostic? Diagnostic,
    PluginKernelDiagnostic? UnsupportedDiagnostic = null);

/// <summary>
/// Equatable carrier for generator-owned not-lowered diagnostics, kept equatable so the incremental generator's
/// caching is preserved.
/// </summary>
internal sealed record HookChainNotLoweredDiagnostic(
    PluginDiagnosticLocation? Location,
    HookChainNotLoweredKind Kind = HookChainNotLoweredKind.RemoteRunLocal,
    bool LocalResultTerminal = false,
    string Detail = "")
{
    private const string ResultMessage =
        "this On<TContext>().Register/RegisterLocal chain could not be lowered to verified IR (the "
        + "context lacks [Hook], the handler returns the wrong result type, or the filter/handler shape "
        + "is unsupported), so the runtime terminal throws";

    public Diagnostic ToDiagnostic()
        => Kind switch
        {
            HookChainNotLoweredKind.RemoteRunLocal => Diagnostic.Create(
                PluginAnalyzerDiagnostics.RunLocalNotLoweredRule,
                Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None),
            HookChainNotLoweredKind.RunChain => Diagnostic.Create(
                PluginAnalyzerDiagnostics.RunChainNotLoweredRule,
                Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None,
                string.IsNullOrEmpty(Detail) ? string.Empty : " (" + Detail + ")"),
            HookChainNotLoweredKind.ResultChain => ResultDiagnostic(),
            _ => throw new InvalidOperationException($"Unsupported not-lowered diagnostic kind '{Kind}'.")
        };

    private Diagnostic ResultDiagnostic()
    {
        var location = Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None;
        return LocalResultTerminal
            ? Diagnostic.Create(PluginAnalyzerDiagnostics.ResultHookNotLoweredRule, location, ResultMessage)
            : Diagnostic.Create(
                PluginAnalyzerDiagnostics.ResultHookNotLoweredRule,
                location,
                DiagnosticSeverity.Warning,
                additionalLocations: null,
                properties: null,
                ResultMessage);
    }
}

internal enum HookChainNotLoweredKind
{
    RemoteRunLocal,
    ResultChain,
    RunChain
}

internal enum HookChainInterceptorInstallKind
{
    GeneratedChain,
    LocalCallback,

    // A result-returning Register chain: the filter and the result-producing handler both lowered to verified IR,
    // installed via UseGeneratedResultChain<TResult>(package, priority). The package reuses the projection package
    // shape (LocalTerminal + ProjectedType = the result record), but the host decodes and returns the Handle
    // result rather than pushing it to a delegate.
    ResultChain,

    // A result-returning RegisterLocal chain: only the filter lowered to verified IR; the result is produced by
    // the plugin-process delegate. Installed via UseGeneratedLocalResultChain<TResult>(package, handler, priority).
    LocalResultChain
}

/// <summary>
/// Everything the generator needs to emit one <c>[InterceptsLocation]</c> method that wires a lowered
/// chain into the pipeline it was called on. All fields are equatable strings/flags so the generator's
/// incrementality is preserved.
/// </summary>
internal sealed record HookChainInterception(
    string AttributeSyntax,
    string ReceiverTypeFullName,
    string HandlerTypeFullName,
    string ReturnTypeFullName,
    string PackageFullName,
    HookChainInterceptorInstallKind InstallKind,
    // True when the lowered local chain has a generated reflection-free decoder, so the interceptor passes
    // <Package>.ReadProjected as the 3rd UseGeneratedLocalChain argument; false keeps the 2-arg reflective form.
    bool HasLocalDecoder = false,
    // Non-null when the generated decoder itself is generic and must be closed with one of this interceptor's
    // inferred type parameters. Used for anonymous terminal projections: the package exposes
    // ReadProjected<TCurrent>, and the generic interceptor passes ReadProjected<TCurrent> explicitly.
    string? LocalDecoderTypeArgument = null,
    // When non-null, the interceptor is emitted as a GENERIC method with this comma-joined type-parameter list
    // (e.g. "TEvent, TCurrent"), and the receiver/handler/return types reference those parameters instead of
    // naming the type arguments. Used when the terminal projection is an anonymous type: it has a real metadata
    // identity (a legal type ARGUMENT Roslyn infers at the call site) but no C#-source-nameable name. The arity
    // must match the interceptable method's generic context, so EVERY receiver type argument becomes a parameter.
    string? InterceptorTypeParameters = null,
    // For a result chain (Register/RegisterLocal): the fully-qualified result type the install entrypoint is
    // closed over (UseGeneratedResultChain<TResult> / UseGeneratedLocalResultChain<TResult>). Null otherwise.
    string? ResultTypeFullName = null,
    // True when the intercepted receiver is a local staged fluent chain. The generated package already contains
    // that receiver's stages, so the interceptor must bypass the runtime stage guard to avoid evaluating them
    // once as native delegates and again inside the sandbox package.
    bool BypassReceiverStage = false);
