using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class IncrementalCacheRemovalTests
{
    private const string Service1Default = """
        using ShaRPC.Core.Attributes;
        using System.Threading.Tasks;

        namespace Demo.Svc
        {
            [ShaRpcService]
            public interface IFooService
            {
                Task<int> AddAsync(int a, int b);
                Task PingAsync();
            }
        }
        """;

    [Fact]
    public void GeneratedExtensions_AreStableWhenSyntaxTreeOrderChanges()
    {
        const string serviceA = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Demo.OrderA
            {
                [ShaRpcService]
                public interface IOrderA
                {
                    Task<int> AAsync();
                }
            }
            """;

        const string serviceB = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Demo.OrderB
            {
                [ShaRpcService]
                public interface IOrderB
                {
                    Task<int> BAsync();
                }
            }
            """;

        var first = GeneratedExtensionsFor(serviceA, serviceB);
        var second = GeneratedExtensionsFor(serviceB, serviceA);

        second.Should().Be(first,
            "the aggregate extensions file should be sorted by service identity, not syntax-tree order");
    }

    [Fact]
    public void RemovingServiceAttribute_DropsServiceFromOutputs()
    {
        var serviceTree = CSharpSyntaxTree.ParseText(Service1Default);
        var compilation = GeneratorTestHelper.CreateCompilation()
            .AddSyntaxTrees(serviceTree);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        driver.GetRunResult().Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName == "Demo_Svc_IFooService.ShaRpcProxy.g.cs");

        var withoutAttr = CSharpSyntaxTree.ParseText("""
            using System.Threading.Tasks;

            namespace Demo.Svc
            {
                public interface IFooService
                {
                    Task<int> AddAsync(int a, int b);
                    Task PingAsync();
                }
            }
            """);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(serviceTree, withoutAttr));
        var result = driver.GetRunResult();

        result.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName == "Demo_Svc_IFooService.ShaRpcProxy.g.cs");
        result.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName == "Demo_Svc_IFooService.ShaRpcDispatcher.g.cs");
        result.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName == "ShaRpcExtensions.g.cs");

        var staleFoo = result.Results.Single().TrackedSteps["Services"]
            .SelectMany(s => s.Outputs)
            .Where(o => InterfaceNameOf(o.Value) == "IFooService")
            .Where(o => o.Reason == IncrementalStepRunReason.Cached ||
                o.Reason == IncrementalStepRunReason.Unchanged)
            .ToArray();
        staleFoo.Should().BeEmpty(
            "IFooService must not flow through Services as cached after its attribute is removed");
    }

    private static string GeneratedExtensionsFor(params string[] sources)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(sources);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        return driver.GetRunResult().Results.Single().GeneratedSources
            .Single(g => g.HintName == "ShaRpcExtensions.g.cs")
            .SourceText.ToString();
    }

    private static string? InterfaceNameOf(object? maybeModel)
    {
        if (maybeModel is null)
        {
            return null;
        }

        return maybeModel.GetType().GetProperty("InterfaceName")?.GetValue(maybeModel) as string;
    }
}
