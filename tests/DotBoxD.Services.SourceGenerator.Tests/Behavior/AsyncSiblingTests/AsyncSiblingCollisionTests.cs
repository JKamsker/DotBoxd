using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using static DotBoxD.Services.SourceGenerator.Tests.Behavior.AsyncSiblingTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Behavior;

public class AsyncSiblingCollisionTests
{
    [Fact]
    public void SyncMethodColliding_WithExistingAsyncName_FiresDBXS004()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace AsyncSibling.D
            {
                [DotBoxDService]
                public interface IClash
                {
                    int Add(int a, int b);
                    Task<int> AddAsync(int a, int b);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var diags = driver.GetRunResult().Diagnostics;

        diags.Should().Contain(d => d.Id == "DBXS004",
            "the sync 'Add' would project to 'AddAsync', which is already declared");
        diags.Where(d => d.Id == "DBXS004")
            .Should().OnlyContain(d => d.Location != Location.None);
    }

    [Fact]
    public void SyncProjectionColliding_WithExistingAsyncCtMethod_DoesNotEmitDuplicateProxyMethod()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.H
            {
                [DotBoxDService]
                public interface IClashCt
                {
                    int Add(int x);
                    Task<int> AddAsync(int x, CancellationToken ct = default);
                }
            }
            """;

        var (asm, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS004");

        var proxy = asm.GetType("AsyncSibling.H.ClashCtProxy")!;
        var addAsyncMethods = proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "AddAsync")
            .Where(m =>
            {
                var parameters = m.GetParameters();
                return parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(int) &&
                    parameters[1].ParameterType == typeof(CancellationToken);
            })
            .ToArray();
        addAsyncMethods.Should().ContainSingle(
            "the original AddAsync implementation should satisfy the sibling signature");
    }

    [Fact]
    public void AsyncProjectionColliding_WithExistingAsyncCtOverload_FiresDBXS004()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace AsyncSibling.J
            {
                [DotBoxDService]
                public interface IAsyncClash
                {
                    [DotBoxDMethod(Name = "FetchNoCt")]
                    Task<int> FetchAsync(int value);
                    [DotBoxDMethod(Name = "FetchWithCt")]
                    Task<int> FetchAsync(int value, CancellationToken ct = default);
                }
            }
            """;

        var (asm, runResult) = Compile(source);

        runResult.Diagnostics.Should().Contain(d => d.Id == "DBXS004" &&
            d.GetMessage().Contains("FetchAsync"));

        var proxy = asm.GetType("AsyncSibling.J.AsyncClashProxy")!;
        proxy.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "FetchAsync")
            .Where(m => m.GetParameters().Length == 2 &&
                m.GetParameters()[1].ParameterType == typeof(CancellationToken))
            .Should().ContainSingle();
    }

    [Fact]
    public void GeneratedSiblingCancellationTokenParameter_AvoidsUserParameterNameCollision()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace AsyncSibling.I
            {
                [DotBoxDService]
                public interface INameCollision
                {
                    int Echo(int ct);
                }
            }
            """;

        var (_, runResult) = Compile(source);

        var generated = runResult.Results.Single().GeneratedSources;
        var sibling = generated
            .Single(g => g.HintName == "AsyncSibling_I_INameCollision.DotBoxDRpcAsync.g.cs")
            .SourceText.ToString();
        sibling.Should().Contain(
            "EchoAsync(int ct, global::System.Threading.CancellationToken ct1 = default);");

        var proxy = generated
            .Single(g => g.HintName == "AsyncSibling_I_INameCollision.DotBoxDRpcProxy.g.cs")
            .SourceText.ToString();
        proxy.Should().Contain(
            "EchoAsync(int ct, global::System.Threading.CancellationToken ct1 = default)");
        proxy.Should().Contain(
            "InvokeAsync<int, int>(\"INameCollision\", \"Echo\", ct, ct1)");
    }

    [Fact]
    public void SiblingInterfaceFile_IsEmittedUnderSiblingHintName()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace AsyncSibling.E
            {
                [DotBoxDService]
                public interface IThing
                {
                    int Compute(int x);
                }
            }
            """;

        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var hints = driver.GetRunResult().Results.Single().GeneratedSources
            .Select(g => g.HintName).OrderBy(h => h).ToArray();
        hints.Should().Contain("AsyncSibling_E_IThing.DotBoxDRpcAsync.g.cs",
            "the sibling interface file goes under its own .DotBoxDRpcAsync.g.cs hint name");
    }

    [Fact]
    public void ServiceInterfaceNameEndingInAsync_DoesNotEmitDuplicateSiblingType_AndCompiles()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace AsyncSibling.G
            {
                [DotBoxDService]
                public interface IFooAsync
                {
                    Task<int> GetAsync();
                }
            }
            """;

        var (asm, runResult) = Compile(source);

        var service = asm.GetType("AsyncSibling.G.IFooAsync")!;
        var proxy = asm.GetType("AsyncSibling.G.FooAsyncProxy")!;
        service.IsAssignableFrom(proxy).Should().BeTrue();

        var hints = runResult.Results.Single().GeneratedSources.Select(g => g.HintName).ToArray();
        hints.Should().Contain("AsyncSibling_G_IFooAsync.DotBoxDRpcProxy.g.cs");
        hints.Should().Contain("AsyncSibling_G_IFooAsync.DotBoxDRpcDispatcher.g.cs");
        hints.Should().NotContain("AsyncSibling_G_IFooAsync.DotBoxDRpcAsync.g.cs",
            "the generated sibling type would have the same name as the user service interface");
    }
}
