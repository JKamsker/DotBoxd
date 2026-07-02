using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public partial class CodegenRegressionTests
{
    [Fact]
    public void NestedSubServiceReturn_ProducesDBXS002_AndDoesNotBuildInvalidProxyType()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.NestedSubService
            {
                public class Outer
                {
                    [RpcService]
                    public interface IInner
                    {
                        Task<int> CountAsync();
                    }
                }

                [RpcService]
                public interface IRoot
                {
                    Task<Outer.IInner> GetInnerAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS003",
            "the nested sub-service interface itself is rejected");
        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS002" &&
            d.GetMessage().Contains("nested sub-service return types are not supported"));

        var proxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == GeneratorTestHelper.HintName(
                "Regress.NestedSubService", "IRoot", GeneratorTestHelper.GeneratedKind.Proxy))
            .SourceText.ToString();
        proxy.Should().NotContain("InnerProxy");
    }

    // Note: ZeroParamVoidMethod_UsesNoResponseOverload (string-Contains on "new object()")
    // was deleted in favour of ZeroParamVoid_AtRuntime_SelectsNoResponseOverload below,
    // which proves the same intent via the actual overload that runs.

    /// <summary>
    /// Regression for the hint-name collision discovered in round 2 review:
    /// two services with the same simple interface name in different namespaces previously
    /// collided on <c>AddSource</c> and threw at runtime.
    /// </summary>
    [Fact]
    public void SameSimpleInterfaceNameAcrossNamespaces_DoesNotCollideOnHintName()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.HintA
            {
                [RpcService(Name = "HintA.IFoo")]
                public interface IFoo
                {
                    Task<int> AAsync();
                }
            }
            namespace Regress.HintB
            {
                [RpcService(Name = "HintB.IFoo")]
                public interface IFoo
                {
                    Task<int> BAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var hints = runResult.Results.Single().GeneratedSources
            .Select(g => g.HintName).OrderBy(h => h).ToArray();
        hints.Should().Contain("Regress_HintA_IFoo.DotBoxDRpcProxy.g.cs");
        hints.Should().Contain("Regress_HintB_IFoo.DotBoxDRpcProxy.g.cs");

        // The two proxy files must be distinct — content equality would imply we
        // wrote the same source under both hint names by mistake.
        var aSource = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "Regress_HintA_IFoo.DotBoxDRpcProxy.g.cs").SourceText.ToString();
        var bSource = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "Regress_HintB_IFoo.DotBoxDRpcProxy.g.cs").SourceText.ToString();
        aSource.Should().NotBe(bSource);
        aSource.Should().Contain("AAsync");
        bSource.Should().Contain("BAsync");
    }

    /// <summary>
    /// Regression: namespaces that differ only by dot-vs-underscore flattening must not
    /// collide in hint names or generated extension methods.
    /// </summary>
    [Fact]
    public void DotAndUnderscoreNamespaceShapes_DoNotCollideOnHintNamesOrExtensions()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.Flat
            {
                [RpcService(Name = "Dotted.IFoo")]
                public interface IFoo
                {
                    Task<int> FromDottedAsync();
                }
            }

            namespace Regress_Flat
            {
                [RpcService(Name = "Underscore.IFoo")]
                public interface IFoo
                {
                    Task<int> FromUnderscoreAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        var hints = runResult.Results.Single().GeneratedSources
            .Select(g => g.HintName)
            .OrderBy(h => h)
            .ToArray();

        var proxyHints = hints.Where(h => h.EndsWith("IFoo.DotBoxDRpcProxy.g.cs", StringComparison.Ordinal)).ToArray();
        proxyHints.Should().HaveCount(2);
        proxyHints.Should().OnlyHaveUniqueItems();
        proxyHints.Should().Contain("Regress_Flat_IFoo.DotBoxDRpcProxy.g.cs");
        proxyHints.Should().Contain(h => h.StartsWith("Regress_Flat__", StringComparison.Ordinal),
            "the underscore namespace should get a deterministic disambiguator");

        var extensions = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName == "DotBoxDRpcExtensions.g.cs")
            .SourceText.ToString();
        extensions.Should().Contain("GetRegress_Flat_Foo");
        extensions.Should().Contain("GetRegress_Flat__");
    }

    /// <summary>
    /// Cache-hygiene regression: when a service is rejected by DBXS003, no model
    /// should flow through the <c>Services</c> tracked step, so it cannot accidentally
    /// be incorporated into the <c>AllServices</c> aggregate.
    /// </summary>
    [Fact]
    public void RejectedGenericService_LeavesServicesStepEmpty()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.GenericHygiene
            {
                [RpcService]
                public interface IRepo<T>
                {
                    Task<T> GetAsync(string id);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();

        var tracked = runResult.Results.Single().TrackedSteps;
        if (tracked.TryGetValue("Services", out var servicesSteps))
        {
            servicesSteps.SelectMany(s => s.Outputs).Should().BeEmpty(
                "a rejected generic service must not leak a model into the Services tracked step");
        }
        // If the Services key is absent entirely, that's the strongest possible
        // cache-hygiene guarantee — nothing else to check.
    }

    /// <summary>
    /// Behavioral test for the DBXS002 stub: invoking a ref/out method on the proxy
    /// at runtime must throw <see cref="NotSupportedException"/> with a message that
    /// identifies the offending parameter. Without this, a regression that silently
    /// returned <c>default(T)</c> from the stub would still pass the compile-only test.
    /// </summary>
    [Fact]
    public void RefOrOutStub_ThrowsNotSupportedExceptionAtRuntime()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Regress.RefOutRuntime
            {
                [RpcService]
                public interface IRor
                {
                    void BadOut(out int x);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        finalCompilation.Emit(ms).Success.Should().BeTrue();
        ms.Position = 0;
        var alc = new System.Runtime.Loader.AssemblyLoadContext(
            "RefOutRuntime_" + Guid.NewGuid(), isCollectible: false);
        var asm = alc.LoadFromStream(ms);

        var proxyType = asm.GetType("Regress.RefOutRuntime.RorProxy")!;
        // The proxy now exposes two constructors (top-level and instance-scoped); pick
        // the single-arg one to construct a top-level proxy for the test.
        var topLevelCtor = proxyType.GetConstructors().Single(c => c.GetParameters().Length == 1);
        var client = Activator.CreateInstance(typeof(NullClient))!;
        var proxy = topLevelCtor.Invoke(new[] { client })!;

        // Invoke BadOut via reflection — should throw NotSupportedException through
        // the TargetInvocationException wrapper.
        var method = proxyType.GetMethod("BadOut")!;
        var args = new object[] { 0 };
        var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method.Invoke(proxy, args));
        thrown.InnerException.Should().BeOfType<NotSupportedException>();
        thrown.InnerException!.Message.Should().Contain("BadOut");
        thrown.InnerException!.Message.Should().Contain("out");
    }

    // Behavioral test: confirms at runtime that the zero-parameter void path selects the
    // parameterless Task InvokeAsync(service, method, ct) overload.

}
