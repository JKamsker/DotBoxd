using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator.Tests;

public class ReviewedCodegenRegressionTests
{
    [Fact]
    public void RefReadonlyReturnMethod_PreservesProxyStubSignature()
    {
        const string source = """
            using ShaRPC.Core.Attributes;

            namespace Regress.RefReadonlyReturn
            {
                [ShaRpcService]
                public interface IRefReadonly
                {
                    ref readonly int Get();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("return value uses an unsupported pass-by-reference kind"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.RefReadonlyReturn",
                "IRefReadonly",
                GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().Contain("public ref readonly int Get()");
    }

    [Fact]
    public void ExtensionMethodSuffixes_AreGloballyCollisionSafe()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace A.B
            {
                [ShaRpcService(Name = "A.B.IFoo")]
                public interface IFoo
                {
                    Task<int> OneAsync();
                }
            }

            namespace C
            {
                [ShaRpcService(Name = "C.IFoo")]
                public interface IFoo
                {
                    Task<int> TwoAsync();
                }
            }

            namespace X
            {
                [ShaRpcService(Name = "X.IA_B_Foo")]
                public interface IA_B_Foo
                {
                    Task<int> ThreeAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var extensions = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "ShaRpcExtensions.g.cs")
            .SourceText.ToString();
        extensions.Should().Contain("GetA_B_Foo");
        extensions.Should().Contain("GetA_B_Foo__1");
        extensions.Should().Contain("GetC_Foo");
    }

    [Fact]
    public void CustomWireNames_WithUnicodeLineSeparators_EmitEscapedStringLiterals()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.UnicodeWireNames
            {
                [ShaRpcService(Name = "svc\u2028name")]
                public interface ILineSeparator
                {
                    [ShaRpcMethod(Name = "method\u2029name")]
                    Task<int> GetAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var dispatcher = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.UnicodeWireNames",
                "ILineSeparator",
                GeneratorTestHelper.GeneratedKind.Dispatcher))
            .SourceText.ToString();
        dispatcher.Should().Contain("\"svc\\u2028name\"");
        dispatcher.Should().Contain("\"method\\u2029name\"");
    }

    [Fact]
    public void GlobalUnderscoreInterface_DoesNotCollideWithNamespacedHintPrefix()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            [ShaRpcService(Name = "Global.A_B")]
            public interface A_B
            {
                Task<int> OneAsync();
            }

            namespace A
            {
                [ShaRpcService(Name = "Namespace.A.B")]
                public interface B
                {
                    Task<int> TwoAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var hints = runResult.Results.Single().GeneratedSources
            .Select(g => g.HintName)
            .ToArray();
        hints.Should().Contain("Global-A_B.ShaRpcProxy.g.cs");
        hints.Should().Contain("A_B.ShaRpcProxy.g.cs");
    }

    [Fact]
    public void CustomWireNames_WithControlEscapes_EmitEscapedProxyAndDispatcherLiterals()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.ControlWireNames
            {
                [ShaRpcService(Name = "svc\u0085\n\r\t\0end")]
                public interface IControlNames
                {
                    [ShaRpcMethod(Name = "method\u0085\n\r\t\0end")]
                    Task<int> GetAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        const string serviceLiteral = "\"svc\\u0085\\n\\r\\t\\0end\"";
        const string methodLiteral = "\"method\\u0085\\n\\r\\t\\0end\"";
        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IControlNames.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain(serviceLiteral);
        proxy.Should().Contain(methodLiteral);

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IControlNames.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().Contain(serviceLiteral);
        dispatcher.Should().Contain("case " + methodLiteral + ":");
    }

    [Fact]
    public void CustomWireNames_WithBackslashesAndUnicodeSeparators_EscapeEveryGeneratedLiteral()
    {
        const string source = """
            using ShaRPC.Core.Attributes;
            using System.Threading.Tasks;

            namespace Regress.SlashedUnicodeWireNames
            {
                [ShaRpcService(Name = "svc\\\u2028name")]
                public interface IBackslashNames
                {
                    [ShaRpcMethod(Name = "method\\\u2029name")]
                    Task<int> GetAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        const string serviceLiteral = @"""svc\\\u2028name""";
        const string methodLiteral = @"""method\\\u2029name""";
        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IBackslashNames.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain(serviceLiteral);
        proxy.Should().Contain(methodLiteral);

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IBackslashNames.ShaRpcDispatcher.g.cs"))
            .SourceText.ToString();
        dispatcher.Should().Contain(serviceLiteral);
        dispatcher.Should().Contain("case " + methodLiteral + ":");
    }

    [Fact]
    public void GenericUnsupportedMethod_PreservesNullableReferenceConstraintOnStub()
    {
        const string source = """
            #nullable enable
            using ShaRPC.Core.Attributes;

            namespace Regress.NullableConstraint
            {
                [ShaRpcService]
                public interface IGenericConstraint
                {
                    T Echo<T>(T value) where T : class?;
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "SHARPC002" &&
            d.GetMessage().Contains("generic service methods are not supported"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IGenericConstraint.ShaRpcProxy.g.cs"))
            .SourceText.ToString();
        proxy.Should().Contain("public T Echo<T>(T value) where T : class?");
    }

    private static (CSharpCompilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        return (finalCompilation, runResult);
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
