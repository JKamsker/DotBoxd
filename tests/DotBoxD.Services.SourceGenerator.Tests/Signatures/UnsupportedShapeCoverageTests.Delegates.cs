using FluentAssertions;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public partial class UnsupportedShapeCoverageTests
{
    [Fact]
    public void DelegatePayloads_ProduceDBXS002_AndSkipDispatch()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedCoverage
            {
                [DotBoxDService]
                public interface IDelegatePayloads
                {
                    Func<int, int> GetTransform();
                    Task<int> ApplyAsync(Func<int, int> transform);
                    Task<int> ApplyCustomAsync(Transform custom);
                    Task<int> ApplyBaseAsync(Delegate callback);
                }

                public delegate int Transform(int value);
            }
            """;

        var (_, runResult) = Compile(source);

        var diagnostics = runResult.Diagnostics
            .Where(d => d.Id == "DBXS002" &&
                d.GetMessage().Contains("delegate type as an RPC payload"))
            .ToArray();
        diagnostics.Should().HaveCount(4);
        diagnostics.Should().Contain(d => d.GetMessage().Contains("return type"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("parameter 'transform'"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("parameter 'custom'"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("parameter 'callback'"));

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IDelegatePayloads.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("GetTransform()");
        proxy.Should().Contain("ApplyAsync(global::System.Func<int, int> transform)");
        proxy.Should().Contain("ApplyCustomAsync(global::Regress.UnsupportedCoverage.Transform custom)");
        proxy.Should().Contain("ApplyBaseAsync(global::System.Delegate callback)");
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IDelegatePayloads.DotBoxDRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"GetTransform\":");
        dispatcher.Should().NotContain("case \"ApplyAsync\":");
        dispatcher.Should().NotContain("case \"ApplyCustomAsync\":");
        dispatcher.Should().NotContain("case \"ApplyBaseAsync\":");
    }
}
