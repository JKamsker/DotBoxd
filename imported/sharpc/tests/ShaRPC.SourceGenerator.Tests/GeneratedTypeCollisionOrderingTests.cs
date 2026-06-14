using System.IO;
using System.Linq;
using FluentAssertions;

namespace ShaRPC.SourceGenerator.Tests;

public class GeneratedTypeCollisionOrderingTests
{
    [Fact]
    public void ExistingAsyncSiblingInterface_DoesNotRejectRootAfterRejectedSubServiceIsStubbed()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GeneratedTypeCollision
            {
                public interface IRootAsync
                {
                }

                [ShaRpcService]
                public interface ISub
                {
                    int Count { get; }
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<ISub> OpenAsync();
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        finalCompilation.Emit(ms).Success.Should().BeTrue();
        runResult.Diagnostics.Should().NotContain(d => d.Id == "SHARPC003" &&
            d.GetMessage().Contains("generated async sibling interface 'IRootAsync'"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains(
                "sub-service return type 'global::Regress.GeneratedTypeCollision.ISub' cannot be proxied"));
        runResult.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName.Contains("IRoot.ShaRpcProxy.g.cs"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IRoot.ShaRpcAsync.g.cs"));
    }
}
