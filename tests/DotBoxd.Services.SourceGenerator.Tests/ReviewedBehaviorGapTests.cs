using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxd.Services.SourceGenerator.Tests;

public class ReviewedBehaviorGapTests
{
    [Fact]
    public void CrossNamespaceValueTaskSubServiceRejectedByGeneratedName_BecomesUnsupportedStub()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading.Tasks;

            namespace Reviewed.GeneratedNameRejected.Sub
            {
                [DotBoxdService]
                public interface IFoo
                {
                    Task<int> AAsync();
                }

                [DotBoxdService]
                public interface Foo
                {
                    Task<int> BAsync();
                }
            }

            namespace Reviewed.GeneratedNameRejected.Root
            {
                [DotBoxdService]
                public interface IRoot
                {
                    ValueTask<Reviewed.GeneratedNameRejected.Sub.IFoo> OpenAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Where(d => d.Id == "DBXS003")
            .Should().HaveCount(2)
            .And.OnlyContain(d => d.GetMessage().Contains("generated proxy and dispatcher type names"));
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("global::Reviewed.GeneratedNameRejected.Sub.IFoo"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxdRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain(
            "global::System.Threading.Tasks.ValueTask<global::Reviewed.GeneratedNameRejected.Sub.IFoo> OpenAsync()");
        proxy.Should().Contain("throw new global::System.NotSupportedException");
        proxy.Should().NotContain("new global::Reviewed.GeneratedNameRejected.Sub.FooProxy");
    }

    [Fact]
    public void InheritedBaseInterfaces_WithSyncAndAsyncCollision_FiresDBXS004()
    {
        const string source = """
            using DotBoxd.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Reviewed.BaseToBaseCollision
            {
                public interface IBaseSync
                {
                    int Fetch(int id);
                }

                public interface IBaseAsync
                {
                    Task<int> FetchAsync(int id, CancellationToken ct = default);
                }

                [DotBoxdService]
                public interface IDerived : IBaseSync, IBaseAsync
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS004" &&
            d.GetMessage().Contains("FetchAsync"));

        var assembly = Load(final);
        var proxy = assembly.GetType("Reviewed.BaseToBaseCollision.DerivedProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync")
            .Should().ContainSingle();
    }

    [Fact]
    public void GenericUnsupportedMethod_PreservesAllConstraintKindsOnStub()
    {
        const string source = """
            #nullable enable
            using DotBoxd.Services.Attributes;

            namespace Reviewed.GenericConstraints
            {
                public class Base
                {
                }

                public interface IFace
                {
                }

                [DotBoxdService]
                public interface IGenericConstraints
                {
                    TStruct StructEcho<TStruct>(TStruct value) where TStruct : struct;
                    TUnmanaged UnmanagedEcho<TUnmanaged>(TUnmanaged value) where TUnmanaged : unmanaged;
                    TNotNull NotNullEcho<TNotNull>(TNotNull value) where TNotNull : notnull;
                    TDerived DerivedEcho<TDerived>(TDerived value) where TDerived : Base, IFace, new();
                    TLeft Pair<TLeft, TRight>(TLeft left, TRight right)
                        where TLeft : IFace
                        where TRight : class, new();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Where(d => d.Id == "DBXS002").Should().HaveCount(5);
        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IGenericConstraints.DotBoxdRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("where TStruct : struct");
        proxy.Should().Contain("where TUnmanaged : unmanaged");
        proxy.Should().Contain("where TNotNull : notnull");
        proxy.Should().Contain(
            "where TDerived : global::Reviewed.GenericConstraints.Base, global::Reviewed.GenericConstraints.IFace, new()");
        proxy.Should().Contain(
            "where TLeft : global::Reviewed.GenericConstraints.IFace where TRight : class, new()");
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

    private static Assembly Load(CSharpCompilation compilation)
    {
        using var ms = new MemoryStream();
        compilation.Emit(ms).Success.Should().BeTrue();
        return Assembly.Load(ms.ToArray());
    }
}
