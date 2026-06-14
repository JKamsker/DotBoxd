using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxd.Services.SourceGenerator.Tests;

public class ReviewedAsyncSiblingProjectionTests
{
    [Fact]
    public void GeneratedCtNameDisambiguation_CollidingWithUnsupportedOriginal_FiresDBXS004()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading;

            namespace AsyncSibling.K
            {
                [DotBoxdService]
                public interface IUnsupportedClash
                {
                    int Fetch(int ct);
                    ref int FetchAsync(int ct, CancellationToken ct1 = default);
                }
            }
            """;

        var (assembly, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS004" &&
            d.GetMessage().Contains("unsupported method 'FetchAsync'"));

        var proxy = assembly.GetType("AsyncSibling.K.UnsupportedClashProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync")
            .Should().ContainSingle();
    }

    [Fact]
    public void VerbatimKeywordProjection_CollidingWithRegularAsyncName_FiresDBXS004()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.KeywordCollision
            {
                [DotBoxdService]
                public interface IKeywords
                {
                    int @class();
                    Task<int> classAsync(CancellationToken ct = default);
                }
            }
            """;

        var (assembly, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS004" &&
            d.GetMessage().Contains("classAsync"));

        var proxy = assembly.GetType("AsyncSibling.KeywordCollision.KeywordsProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "classAsync")
            .Should().ContainSingle();
    }

    [Fact]
    public void UnsupportedGenericOriginal_DoesNotBlockNonGenericAsyncProjection()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.GenericArity
            {
                [DotBoxdService]
                public interface IGenericOriginal
                {
                    int Fetch(int id);
                    Task<T> FetchAsync<T>(int id, CancellationToken ct = default);
                }
            }
            """;

        var (assembly, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("generic service methods are not supported"));
        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS004");

        var proxy = assembly.GetType("AsyncSibling.GenericArity.GenericOriginalProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync")
            .Should().HaveCount(2);
    }

    [Fact]
    public void InheritedSyncMethod_CollidingWithDerivedAsyncMethod_FiresDBXS004()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.InheritedCollision
            {
                public interface IBase
                {
                    int Fetch(int id);
                }

                [DotBoxdService]
                public interface IDerived : IBase
                {
                    Task<int> FetchAsync(int id, CancellationToken ct = default);
                }
            }
            """;

        var (assembly, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS004" &&
            d.GetMessage().Contains("FetchAsync"));

        var proxy = assembly.GetType("AsyncSibling.InheritedCollision.DerivedProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync")
            .Should().ContainSingle();
    }

    [Fact]
    public void SyncMethodAlreadyEndingAsync_WithCancellationToken_DoesNotEmitDuplicateProxyMethod()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading;

            namespace AsyncSibling.SelfCollision
            {
                [DotBoxdService]
                public interface IFoo
                {
                    int FetchAsync(CancellationToken ct = default);
                }
            }
            """;

        var (assembly, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS004" &&
            d.GetMessage().Contains("FetchAsync"));

        var proxy = assembly.GetType("AsyncSibling.SelfCollision.FooProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync")
            .Should().ContainSingle();
    }

    private static (Assembly Assembly, GeneratorDriverRunResult RunResult) Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
        ms.Position = 0;
        return (Assembly.Load(ms.ToArray()), runResult);
    }
}
