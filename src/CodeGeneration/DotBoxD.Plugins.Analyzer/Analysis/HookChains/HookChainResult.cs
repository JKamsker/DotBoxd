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
    HookChainInterceptorInstallKind InstallKind);
