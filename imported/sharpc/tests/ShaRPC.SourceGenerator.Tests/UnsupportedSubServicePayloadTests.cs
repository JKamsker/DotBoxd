using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class UnsupportedSubServicePayloadTests
{
    [Fact]
    public void SubServiceParameter_BecomesUnsupportedStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedSubPayload
            {
                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task SendAsync(ISub sub);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        AssertUnsupportedStub(runResult, "SendAsync");
    }

    [Fact]
    public void CollectionContainingSubServiceReturn_BecomesUnsupportedStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedSubPayload
            {
                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<IList<ISub>> ListAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        AssertUnsupportedStub(runResult, "ListAsync");
    }

    [Fact]
    public void DtoContainingSubServiceReturn_BecomesUnsupportedStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedSubPayload
            {
                public sealed record Result(ISub Inner);

                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<Result> GetAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        AssertUnsupportedStub(runResult, "GetAsync");
    }

    [Fact]
    public void DtoContainingSubServiceParameter_BecomesUnsupportedStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedSubPayload
            {
                public sealed record Request(ISub Inner);

                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task SendAsync(Request request);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        AssertUnsupportedStub(runResult, "SendAsync");
    }

    [Fact]
    public void NestedDtoPublicFieldContainingSubService_BecomesUnsupportedStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedSubPayload
            {
                public sealed class Inner
                {
                    public ISub? Sub;
                }

                public sealed record Outer(Inner Inner);

                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<Outer> GetAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        AssertUnsupportedStub(runResult, "GetAsync");
    }

    [Fact]
    public void InheritedDtoMemberContainingSubService_BecomesUnsupportedStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedSubPayload
            {
                public class BaseRequest
                {
                    public ISub? Sub { get; init; }
                }

                public sealed class Request : BaseRequest
                {
                    public int Id { get; init; }
                }

                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task SendAsync(Request request);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        AssertUnsupportedStub(runResult, "SendAsync");
    }

    [Fact]
    public void RecursiveGenericDtoContainingSubService_BecomesUnsupportedStub()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnsupportedSubPayload
            {
                public sealed class Node<T>
                {
                    public Node<ISub> SubNode { get; init; } = null!;
                }

                [ShaRpcService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                [ShaRpcService]
                public interface IRoot
                {
                    Task<Node<int>> GetAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        AssertUnsupportedStub(runResult, "GetAsync");
    }

    private static void AssertUnsupportedStub(GeneratorDriverRunResult runResult, string methodName)
    {
        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("contains a sub-service type"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("throw new global::System.NotSupportedException");

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().NotContain("case \"" + methodName + "\":");
    }

    private static (CSharpCompilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (compilation.AddSyntaxTrees(runResult.GeneratedTrees), runResult);
    }

    private static void AssertCompiles(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
    }
}
