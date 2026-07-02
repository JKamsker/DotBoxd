using FluentAssertions;

namespace DotBoxD.Services.SourceGenerator.Tests.Collisions;

public class GeneratedTypeCollisionOrderingTests
{
    [Fact]
    public void ExistingAsyncSiblingInterface_DoesNotRejectRootAfterRejectedSubServiceIsStubbed()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GeneratedTypeCollision
            {
                public interface IRootAsync
                {
                }

                [RpcService]
                public interface ISub
                {
                    int Count { get; }
                }

                [RpcService]
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
        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated async sibling interface 'IRootAsync'"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains(
                "sub-service return type 'global::Regress.GeneratedTypeCollision.ISub' cannot be proxied"));
        runResult.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName.Contains("IRoot.DotBoxDRpcProxy.g.cs"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IRoot.DotBoxDRpcAsync.g.cs"));
    }
}
