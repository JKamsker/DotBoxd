using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generation;

public sealed class LoweringNegativeShapeMatrixTests
{
    public static IEnumerable<object[]> UnsupportedLoweringShapes()
    {
        yield return
        [
            "event kernel unsupported expression",
            "DBXK100",
            DiagnosticSeverity.Error,
            """
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId, string Message, int Distance);

            [Plugin("unsupported-kernel-expression")]
            public sealed partial class UnsupportedKernel : IEventKernel<DamageEvent>
            {
                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.TargetId.ToUpperInvariant() == "BOSS";

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """
        ];
        yield return
        [
            "server extension unsupported RPC expression",
            "DBXK100",
            DiagnosticSeverity.Error,
            """
            using System.Collections.Generic;
            using System.Linq;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("unsupported-rpc-expression")]
            public sealed partial class UnsupportedRpcKernel
            {
                public int Run(List<int> values, HookContext ctx)
                {
                    return values.Sum();
                }
            }
            """
        ];
        yield return
        [
            "remote RunLocal unsupported predicate",
            "DBXK111",
            DiagnosticSeverity.Info,
            """
            using System.Collections.Generic;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record ScoreEvent(int Distance, List<int> Scores);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<ScoreEvent>()
                        .Where(e => e.Scores == e.Scores)
                        .RunLocal((e, ctx) => { });
            }
            """
        ];
        yield return
        [
            "Run hook chain unsupported predicate",
            "DBXK114",
            DiagnosticSeverity.Warning,
            """
            using System.Collections.Generic;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record ScoreEvent(string TargetId, int Distance, List<int> Scores);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<ScoreEvent>()
                        .Where(e => e.Scores == e.Scores)
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "ok"));
            }
            """
        ];
        yield return
        [
            "result hook wrong result type",
            "DBXK113",
            DiagnosticSeverity.Info,
            """
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            [HookResult]
            public readonly partial record struct OtherResult(bool Success, string? Reason);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .RegisterLocal((ctx, hookContext) => new OtherResult { Success = true }, 0);
            }
            """
        ];
    }

    [Theory]
    [MemberData(nameof(UnsupportedLoweringShapes))]
    public void Unsupported_lowering_shapes_report_stable_DBXK_diagnostics(
        string name,
        string diagnosticId,
        DiagnosticSeverity severity,
        string source)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);
        var diagnostic = Assert.Single(
            diagnostics,
            item => string.Equals(item.Id, diagnosticId, StringComparison.Ordinal));

        Assert.Equal(severity, diagnostic.Severity);
        Assert.DoesNotContain(diagnostics, item => string.Equals(item.Id, "DBXK117", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(name));
    }
}
