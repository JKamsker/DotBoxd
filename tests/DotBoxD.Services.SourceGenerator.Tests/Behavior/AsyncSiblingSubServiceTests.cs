using FluentAssertions;

namespace DotBoxD.Services.SourceGenerator.Tests.Behavior;

public sealed class AsyncSiblingSubServiceTests
{
    [Fact]
    public void SyncSubServiceMethod_ProjectsToTaskOfSubService_OnAsyncSibling()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace AsyncSibling.SubServices
            {
                [DotBoxDService]
                public interface IRoot
                {
                    ISub Open();
                }

                [DotBoxDService]
                public interface ISub
                {
                    int Count();
                }
            }
            """;

        var (driver, _) = GeneratorTestHelper.RunGenerator(source);

        var sibling = driver.GetRunResult().Results.Single().GeneratedSources
            .Single(source => source.HintName == "AsyncSibling_SubServices_IRoot.DotBoxDRpcAsync.g.cs")
            .SourceText
            .ToString();
        sibling.Should().Contain(
            "global::System.Threading.Tasks.Task<global::AsyncSibling.SubServices.ISub> OpenAsync(");
    }
}
