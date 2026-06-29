namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

public sealed class PluginAnalyzerHookAttributeValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_hook_name_reports_DBXK100(string hookName)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics($$"""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("{{hookName}}", typeof(DamageResult))]
            public sealed record DamageEvent(string TargetId, string Message);

            public readonly record struct DamageResult(bool Success, string? Reason);

            [Plugin("blank-hook")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("[Hook] name", StringComparison.Ordinal) &&
                 d.GetMessage().Contains("must not be empty or whitespace", StringComparison.Ordinal));
    }
}
