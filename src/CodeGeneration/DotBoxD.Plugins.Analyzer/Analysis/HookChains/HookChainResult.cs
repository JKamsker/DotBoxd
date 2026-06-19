namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

/// <summary>
/// A lowered hook chain: the package model (emitted like a kernel) plus optional interception metadata
/// for the C# interceptor that replaces the <c>Run(lambda)</c> call site with
/// <c>UseGeneratedChain</c>. Interception is null when the call site has no interceptable location —
/// the package still generates.
/// </summary>
internal sealed record HookChainResult(PluginKernelModel Model, HookChainInterception? Interception);

internal enum HookChainInterceptorInstallKind
{
    GeneratedChain,
    LocalCallback
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
    // When non-null, the interceptor is emitted as a GENERIC method with this comma-joined type-parameter list
    // (e.g. "TEvent, TCurrent"), and the receiver/handler/return types reference those parameters instead of
    // naming the type arguments. Used when the terminal projection is an anonymous type: it has a real metadata
    // identity (a legal type ARGUMENT Roslyn infers at the call site) but no C#-source-nameable name. The arity
    // must match the interceptable method's generic context, so EVERY receiver type argument becomes a parameter.
    string? InterceptorTypeParameters = null);
