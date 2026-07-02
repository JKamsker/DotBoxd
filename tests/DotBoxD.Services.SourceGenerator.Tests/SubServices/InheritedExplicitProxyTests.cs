using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.SubServices;

public class InheritedExplicitProxyTests
{
    [Fact]
    public void InheritedMethodsCollidingWithProxyMembers_UseDeclaringInterface()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.InheritedExplicitProxy
            {
                public interface IBase
                {
                    int FooProxy();
                    int _invoker();
                    int _instanceId();
                }

                [DotBoxDService]
                public interface IFoo : IBase
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = GetProxy(runResult);
        proxy.Should().Contain("int global::Regress.InheritedExplicitProxy.IBase.FooProxy()");
        proxy.Should().Contain("int global::Regress.InheritedExplicitProxy.IBase._invoker()");
        proxy.Should().Contain("int global::Regress.InheritedExplicitProxy.IBase._instanceId()");
        proxy.Should().NotContain("global::Regress.InheritedExplicitProxy.IFoo.FooProxy()");
    }

    [Fact]
    public void ObjectMemberName_UsesExplicitImplementationWithoutHidingWarning()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;

            namespace Regress.ObjectMemberProxy
            {
                [DotBoxDService]
                public interface IFoo
                {
                    Type GetType();
                }
            }
            """;

        var (final, runResult) = Run(source);
        using var ms = new MemoryStream();
        var emit = final.Emit(ms);

        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
        emit.Diagnostics.Should().NotContain(d => d.Id == "CS0108");
        GetProxy(runResult).Should().Contain(
            "global::System.Type global::Regress.ObjectMemberProxy.IFoo.GetType()");
    }

    [Fact]
    public void DuplicateInheritedExplicitMethods_ImplementEveryDeclaringInterface()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;

            namespace Regress.DuplicateInheritedExplicitProxy
            {
                public interface ILeft
                {
                    Type GetType();
                }

                public interface IRight
                {
                    Type GetType();
                }

                [DotBoxDService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var proxy = GetProxy(runResult);
        proxy.Should().Contain(
            "global::System.Type global::Regress.DuplicateInheritedExplicitProxy.ILeft.GetType()");
        proxy.Should().Contain(
            "global::System.Type global::Regress.DuplicateInheritedExplicitProxy.IRight.GetType()");
    }

    [Fact]
    public void DuplicateInheritedMethodsWithDifferentWireNames_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedWireNames
            {
                public interface ILeft
                {
                    [DotBoxDMethod(Name = "left")]
                    int Get();
                }

                public interface IRight
                {
                    [DotBoxDMethod(Name = "right")]
                    int Get();
                }

                [DotBoxDService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("different wire method name"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    [Fact]
    public void DuplicateInheritedMethodsWithSameEffectiveWireName_Deduplicate()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.DuplicateInheritedSameWireName
            {
                public interface ILeft
                {
                    [DotBoxDMethod(Name = "Get")]
                    int Get();
                }

                public interface IRight
                {
                    int Get();
                }

                [DotBoxDService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        GetProxy(runResult).Should().Contain("public int Get()");
    }

    [Fact]
    public void DuplicateInheritedSubServiceProperties_Deduplicate()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.DuplicateInheritedSubServiceProperty
            {
                [DotBoxDService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                public interface ILeft
                {
                    ISub Child { get; }
                }

                public interface IRight
                {
                    ISub Child { get; }
                }

                [DotBoxDService]
                public interface IRoot : ILeft, IRight
                {
                }
            }
            """;

        var (final, runResult) = Run(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        AssertCompiles(final);

        var extensions = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "DotBoxDRpcExtensions.g.cs")
            .SourceText.ToString();
        System.Text.RegularExpressions.Regex.Matches(extensions, @"peer\.ProvideSub\(")
            .Should().HaveCount(1);
        System.Text.RegularExpressions.Regex.Matches(extensions, @"\.Child\b")
            .Should().HaveCount(1);
    }

    [Fact]
    public void DuplicateInheritedMethodsWithDifferentNullableAnnotations_RejectService()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.DuplicateInheritedNullableAnnotations
            {
                [DotBoxDService]
                public interface ISub
                {
                    Task<int> CountAsync();
                }

                public interface ILeft
                {
                    Task<ISub> OpenAsync();
                }

                public interface IRight
                {
                    Task<ISub?> OpenAsync();
                }

                [DotBoxDService]
                public interface IFoo : ILeft, IRight
                {
                }
            }
            """;

        var (_, runResult) = Run(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003" &&
            d.GetMessage().Contains("incompatible nullable annotations"));
        runResult.Results.Single().GeneratedSources
            .Should().NotContain(g => g.HintName.Contains("IFoo."));
    }

    private static string GetProxy(GeneratorDriverRunResult runResult) =>
        runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IFoo.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();

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
