using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginAnalyzerDiagnostics
{
    internal const string ShippedRulesHelpLinkBase =
        "https://github.com/JKamsker/Safe-IR/blob/main/src/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Shipped.md#";

    internal const string UnshippedRulesHelpLinkBase =
        "https://github.com/JKamsker/Safe-IR/blob/main/src/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Unshipped.md#";

    public static readonly DiagnosticDescriptor UnsupportedKernelShapeRule = new(
        "DBXK100",
        "Plugin kernel shape is not supported",
        "{0}",
        "DotBoxD.Kernels.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Plugin package generation supports a restricted kernel expression subset; interpolation holes may be strings or supported invariant string-convertible numeric types.",
        helpLinkUri: UnshippedRulesHelpLinkBase + "DBXK100");

    // A remote RunLocal chain (RemoteHookRegistry / RemoteSubscriptionRegistry) is only intercepted when its
    // Where/Select stages lower to verified IR. When a stage cannot be lowered (an unsupported projection or
    // predicate) the generator fails safe and emits no interceptor, so the native terminal throws
    // NotSupportedException at runtime. This surfaces that cause at compile time instead of leaving a silent skip.
    public static readonly DiagnosticDescriptor RunLocalNotLoweredRule = new(
        "DBXK111",
        "RunLocal chain is not lowered and will throw at runtime",
        "This remote RunLocal chain could not be lowered to verified IR (an unsupported Where/Select projection or "
            + "predicate), so the generator does not intercept it and the runtime terminal throws "
            + "NotSupportedException; use a supported projection/predicate shape",
        "DotBoxD.Kernels.Generation",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A recognized remote RunLocal hook chain whose Where/Select stages cannot be lowered is skipped "
            + "by the generator; without interception its native terminal throws at runtime.",
        helpLinkUri: UnshippedRulesHelpLinkBase + "DBXK111");

    // A [HookResult] record must declare the control fields the generated builders (and the runtime
    // abstain/fallthrough contract) depend on. Without them the builders cannot be emitted, so surface the
    // missing contract instead of leaving Ok()/Reject() undefined.
    public static readonly DiagnosticDescriptor HookResultContractRule = new(
        "DBXK112",
        "Hook result type does not satisfy the result contract",
        "{0}",
        "DotBoxD.Kernels.Generation",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A [HookResult] type must be a top-level readonly record struct that declares a 'bool Success' "
            + "field and a 'string? Reason' field, so the generated Ok()/Reject() builders and the runtime "
            + "abstain/fallthrough contract are well-defined.",
        helpLinkUri: UnshippedRulesHelpLinkBase + "DBXK112");

    // A result-returning hook chain (On<TContext>().…​.Register/RegisterLocal) is only intercepted when its
    // context carries [Hook], its handler returns the associated result type, and its filter/handler lower to
    // verified IR. When any of those fail the generator emits no interceptor and the runtime terminal throws,
    // so surface the cause at build time rather than leaving a silent skip.
    public static readonly DiagnosticDescriptor ResultHookNotLoweredRule = new(
        "DBXK113",
        "Result hook chain is not lowered and will throw at runtime",
        "{0}",
        "DotBoxD.Kernels.Generation",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A recognized result-returning hook chain whose context lacks [Hook], whose handler returns the "
            + "wrong result type, or whose filter/handler cannot be lowered is skipped by the generator; without "
            + "interception its native Register/RegisterLocal terminal throws at runtime.",
        helpLinkUri: UnshippedRulesHelpLinkBase + "DBXK113");
}
