using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Reviews;

public class ReviewedAggregateIncrementalityTests
{
    [Fact]
    public void AddingDuplicateWireName_KeepsStableServiceCached()
    {
        var original = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            namespace Reviewed.IncrementalWire
            {
                [RpcService(Name = "a")] public interface IA { int A(); }
                [RpcService(Name = "b")] public interface IB { int B(); }
                [RpcService(Name = "stable")] public interface IStable { int S(); }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(original);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var duplicate = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            namespace Reviewed.IncrementalWire
            {
                [RpcService(Name = "a")] public interface IA { int A(); }
                [RpcService(Name = "a")] public interface IB { int B(); }
                [RpcService(Name = "stable")] public interface IStable { int S(); }
            }
            """);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(original, duplicate));
        var result = driver.GetRunResult();

        StepReasons(result, "WireServiceNames").Should().Contain(r => IsChanged(r));
        StepReasons(result, "RejectedServices").Should().Contain(r => IsChanged(r));
        StepReasons(result, "ServiceResults").Should().Contain(r => IsChanged(r));
        AssertInterfaceOutputCached(result, "Services", "IStable");
        AssertInterfaceOutputCached(result, "ServiceBundles", "IStable");
        AssertStableSourceExistsAndSomeSourceOutputCached(result);
    }

    [Fact]
    public void AddingGeneratedServiceNameCollision_KeepsStableServiceCached()
    {
        var original = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            namespace Reviewed.IncrementalGenerated
            {
                [RpcService] public interface IFoo { int A(); }
                [RpcService] public interface IBar { int B(); }
                [RpcService] public interface IStable { int S(); }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(original);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var duplicate = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            namespace Reviewed.IncrementalGenerated
            {
                [RpcService] public interface IFoo { int A(); }
                [RpcService] public interface Foo { int B(); }
                [RpcService] public interface IStable { int S(); }
            }
            """);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(original, duplicate));
        var result = driver.GetRunResult();

        StepReasons(result, "GeneratedServiceNames").Should().Contain(r => IsChanged(r));
        StepReasons(result, "RejectedServices").Should().Contain(r => IsChanged(r));
        StepReasons(result, "ServiceResults").Should().Contain(r => IsChanged(r));
        AssertInterfaceOutputCached(result, "Services", "IStable");
        AssertInterfaceOutputCached(result, "ServiceBundles", "IStable");
        AssertStableSourceExistsAndSomeSourceOutputCached(result);
    }

    [Fact]
    public void RejectingSubService_KeepsNameAggregatesAndStableServiceCached()
    {
        var original = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;
            namespace Reviewed.IncrementalRejectedSub
            {
                [RpcService] public interface ISub { Task<int> CountAsync(); }
                [RpcService] public interface IRoot { Task<ISub> OpenAsync(); }
                [RpcService] public interface IStable { int S(); }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(original);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var rejected = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;
            namespace Reviewed.IncrementalRejectedSub
            {
                [RpcService] public interface ISub { int Count { get; } }
                [RpcService] public interface IRoot { Task<ISub> OpenAsync(); }
                [RpcService] public interface IStable { int S(); }
            }
            """);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(original, rejected));
        var result = driver.GetRunResult();

        StepReasons(result, "WireServiceNames").Should().OnlyContain(r => IsCachedOrUnchanged(r));
        StepReasons(result, "GeneratedServiceNames").Should().OnlyContain(r => IsCachedOrUnchanged(r));
        StepReasons(result, "RejectedServices").Should().Contain(r => IsChanged(r));
        StepReasons(result, "ServiceResults").Should().Contain(r => IsChanged(r));
        AssertInterfaceOutputCached(result, "Services", "IStable");
        AssertInterfaceOutputCached(result, "ServiceBundles", "IStable");
        AssertStableSourceExistsAndSomeSourceOutputCached(result);
    }

    [Fact]
    public void FinalRejectedSubServiceChange_KeepsStableServiceCached()
    {
        var original = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;
            namespace Reviewed.IncrementalFinalRejected
            {
                [RpcService] public interface ISub { int Count(); }
                [RpcService] public interface IRoot { Task<ISub> OpenAsync(); }
                [RpcService] public interface IStable { int S(); }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(original);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var rejected = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;
            namespace Reviewed.IncrementalFinalRejected
            {
                public interface ISubAsync { }
                [RpcService] public interface ISub { int Count(); }
                [RpcService] public interface IRoot { Task<ISub> OpenAsync(); }
                [RpcService] public interface IStable { int S(); }
            }
            """);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(original, rejected));
        var result = driver.GetRunResult();

        StepReasons(result, "FinalRejectedServices").Should().Contain(r => IsChanged(r));
        StepReasons(result, "FinalSubServiceValidatedServiceResults").Should()
            .Contain(r => IsChanged(r));
        AssertInterfaceOutputCached(result, "Services", "IStable");
        AssertInterfaceOutputCached(result, "ServiceBundles", "IStable");
        AssertStableSourceExistsAndSomeSourceOutputCached(result);
    }

    [Fact]
    public void WireNameOnlyEdit_DoesNotInvalidateFinalRejectedServices()
    {
        var original = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;
            namespace Reviewed.IncrementalFinalRejectedShape
            {
                [RpcService] public interface ISub { int Count(); }
                [RpcService] public interface IRoot { Task<ISub> OpenAsync(); }
                [RpcService] public interface IStable { int S(); }
            }
            """);
        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(original);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);

        var renamedWireMethod = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;
            namespace Reviewed.IncrementalFinalRejectedShape
            {
                [RpcService] public interface ISub { int Count(); }
                [RpcService] public interface IRoot { Task<ISub> OpenAsync(); }
                [RpcService] public interface IStable { [RpcMethod(Name = "Stable")] int S(); }
            }
            """);

        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(original, renamedWireMethod));
        var result = driver.GetRunResult();

        StepReasons(result, "FinalRejectedServices").Should().OnlyContain(r => IsCachedOrUnchanged(r));
        AssertInterfaceOutputCached(result, "Services", "IRoot");
        AssertInterfaceOutputCached(result, "ServiceBundles", "IRoot");
    }

    private static IncrementalStepRunReason[] StepReasons(
        GeneratorDriverRunResult result,
        string trackingName) =>
        result.Results.Single().TrackedSteps[trackingName]
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToArray();

    private static void AssertInterfaceOutputCached(
        GeneratorDriverRunResult result,
        string trackingName,
        string interfaceName)
    {
        var output = result.Results.Single().TrackedSteps[trackingName]
            .SelectMany(s => s.Outputs)
            .Single(o => InterfaceNameOf(o.Value) == interfaceName);
        IsCachedOrUnchanged(output.Reason).Should().BeTrue();
    }

    private static void AssertStableSourceExistsAndSomeSourceOutputCached(GeneratorDriverRunResult result)
    {
        result.Results.Single().GeneratedSources
            .Should().Contain(g => g.HintName.Contains("IStable.DotBoxDRpcProxy.g.cs"));

        var outputs = result.Results.Single().TrackedOutputSteps
            .SelectMany(kvp => kvp.Value)
            .SelectMany(step => step.Outputs)
            .ToArray();
        outputs.Should().NotBeEmpty();
        outputs.Should().Contain(o => IsCachedOrUnchanged(o.Reason));
    }

    private static bool IsChanged(IncrementalStepRunReason reason) =>
        reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New or IncrementalStepRunReason.Removed;

    private static bool IsCachedOrUnchanged(IncrementalStepRunReason reason) =>
        reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged;

    private static string? InterfaceNameOf(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var direct = value.GetType().GetProperty("InterfaceName")?.GetValue(value) as string;
        if (direct is not null)
        {
            return direct;
        }

        var model = value.GetType().GetProperty("Model")?.GetValue(value);
        return model?.GetType().GetProperty("InterfaceName")?.GetValue(model) as string;
    }
}
