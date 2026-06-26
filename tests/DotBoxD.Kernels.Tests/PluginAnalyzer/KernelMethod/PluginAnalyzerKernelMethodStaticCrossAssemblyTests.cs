namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

/// <summary>
/// A static <c>[KernelMethod]</c> helper defined in a referenced (metadata-only) assembly cannot be inlined:
/// only instance helpers on a generated server context cross assemblies (via a generated descriptor). The
/// generator must fail closed with an actionable diagnostic that names the asymmetry, not the misleading
/// "must be declared in source" message (the helper IS in source — just in another assembly).
/// </summary>
public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    private const string StaticCrossAssemblySdk = """
        using DotBoxD.Abstractions;

        namespace CrossAssemblyStatic;

        public static class StaticGate
        {
            [KernelMethod]
            public static bool IsClose(int distance) => distance <= 5;
        }
        """;

    private const string StaticCrossAssemblyConsumer = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;
        using CrossAssemblyStatic;

        namespace Sample;

        public sealed record AggroEvent(string MonsterId, string Message, int Distance);

        [Plugin("cross-assembly-static")]
        public sealed partial class CrossAssemblyStaticKernel : IEventKernel<AggroEvent>
        {
            public bool ShouldHandle(AggroEvent e, HookContext ctx)
                => StaticGate.IsClose(e.Distance);

            public void Handle(AggroEvent e, HookContext ctx)
                => ctx.Messages.Send(e.MonsterId, e.Message);
        }
        """;

    [Fact]
    public void Metadata_only_static_kernel_method_reports_actionable_cross_assembly_diagnostic()
    {
        var sdkReference = CompilePlainReference(StaticCrossAssemblySdk, "CrossAssemblyStaticSdk");
        var diagnostics = GeneratorDiagnostics(StaticCrossAssemblyConsumer, sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "cannot be inlined across assemblies",
                              StringComparison.Ordinal));
    }
}
