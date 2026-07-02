using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace DotBoxD.Services.SourceGenerator.Tests.Coverage;

public partial class SourceGenUtilityCoverageTests
{
    [Fact]
    public void ExistingTypeCollision_ReportsDiagnosticAtUserTypeLocation()
    {
        var collision = CSharpSyntaxTree.ParseText("""
            namespace Collide.Single
            {
                public sealed class ThingProxy { }
            }
            """, path: "UserType.cs");

        var service = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;

            namespace Collide.Single
            {
                [RpcService]
                public interface IThing { int Bar(); }
            }
            """, path: "Service.cs");

        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(collision, service);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        var diagnostic = runResult.Diagnostics.Single(d =>
            d.Id == "DBXS003" &&
            d.GetMessage().Contains("generated proxy type 'ThingProxy' would collide"));
        diagnostic.Location.GetLineSpan().Path.Should().Be("UserType.cs",
            "the collision location must come from the pre-existing user declaration");
    }

    [Fact]
    public void DuplicateExistingTypeDeclarations_DedupAndPickStableLocation()
    {
        // Two identical declarations of the colliding type in different files. The index
        // must dedup by key and break the location tie deterministically by file path,
        // so the diagnostic anchors to "A.cs" no matter the syntax-tree order.
        const string colliding = """
            namespace Collide.Dup
            {
                public sealed class ThingProxy { }
            }
            """;
        var bCs = CSharpSyntaxTree.ParseText(colliding, path: "B.cs");
        var aCs = CSharpSyntaxTree.ParseText(colliding, path: "A.cs");
        var service = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;

            namespace Collide.Dup
            {
                [RpcService]
                public interface IThing { int Bar(); }
            }
            """, path: "Service.cs");

        var forward = DiagnosticPathFor(bCs, service, aCs);
        var reversed = DiagnosticPathFor(aCs, service, bCs);

        forward.Should().Be("A.cs");
        reversed.Should().Be("A.cs",
            "duplicate existing-type locations must tie-break by path regardless of order");
    }

    [Fact]
    public void MultipleDistinctCollisions_AllResolveViaLocationIndexBinarySearch()
    {
        // Several distinct collisions exercise Find()'s binary search across a sorted,
        // de-duplicated index where the search target sits at varied positions.
        var collisions = CSharpSyntaxTree.ParseText("""
            namespace Collide.Many
            {
                public sealed class AaaProxy { }
                public sealed class MmmProxy { }
                public sealed class ZzzProxy { }
            }
            """, path: "Existing.cs");

        var services = CSharpSyntaxTree.ParseText("""
            using DotBoxD.Services.Attributes;

            namespace Collide.Many
            {
                [RpcService]
                public interface IAaa { int A(); }

                [RpcService]
                public interface IMmm { int M(); }

                [RpcService]
                public interface IZzz { int Z(); }
            }
            """, path: "Services.cs");

        var compilation = GeneratorTestHelper.CreateCompilation().AddSyntaxTrees(collisions, services);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        foreach (var name in new[] { "AaaProxy", "MmmProxy", "ZzzProxy" })
        {
            var diagnostic = runResult.Diagnostics.Single(d =>
                d.Id == "DBXS003" &&
                d.GetMessage().Contains($"generated proxy type '{name}' would collide"));
            diagnostic.Location.GetLineSpan().Path.Should().Be("Existing.cs");
        }
    }

    // ---------------------------------------------------------------------
    // FinalRejectionMethodParameters: a sub-service that gets finally rejected makes
    // the root method a NotSupported stub. The parameter builder runs in two modes:
    // an async method that already declares a CancellationToken (early return of the
    // existing list) and a method that needs a synthesized trailing `ct`.
    // ---------------------------------------------------------------------

    [Fact]
    public void FinalRejectedSubService_WithExistingCancellationToken_StubsRootMethod()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading;
            using System.Threading.Tasks;

            namespace FinalReject.WithCt
            {
                // Pre-existing async-sibling interface forces a generated-name collision that
                // finally rejects ISub, which in turn stubs IRoot.OpenAsync.
                public interface ISubAsync { }

                [RpcService]
                public interface ISub
                {
                    int Count();
                }

                [RpcService]
                public interface IRoot
                {
                    Task<ISub> OpenAsync(int id, CancellationToken ct = default);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "DBXS002" && d.GetMessage().Contains("IRoot.OpenAsync"));

        var rootProxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        rootProxy.Should().Contain("throw new global::System.NotSupportedException");
        // The original CancellationToken parameter must be preserved on the stub signature.
        rootProxy.Should().Contain("OpenAsync(int id");
        rootProxy.Should().Contain("global::System.Threading.CancellationToken");
    }

    [Fact]
    public void FinalRejectedSubService_WithoutCancellationToken_StubSynthesizesTrailingCt()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace FinalReject.NoCt
            {
                public interface ISubAsync { }

                [RpcService]
                public interface ISub
                {
                    int Count();
                }

                [RpcService]
                public interface IRoot
                {
                    // No CancellationToken declared: the stub builder must synthesize a
                    // trailing ct parameter for the generated async sibling stub.
                    Task<ISub> OpenAsync(int id);
                }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().Contain(d =>
            d.Id == "DBXS002" && d.GetMessage().Contains("IRoot.OpenAsync"));

        var rootProxy = runResult.Results.Single().GeneratedSources
            .Single(g => g.HintName.EndsWith("IRoot.DotBoxDRpcProxy.g.cs"))
            .SourceText.ToString();
        rootProxy.Should().Contain("throw new global::System.NotSupportedException");
        rootProxy.Should().Contain("OpenAsync(int id");
    }

    // ---------------------------------------------------------------------
    // TupleElementNameComparer: inherited duplicate methods with tuple types. These
    // exercise array element-name comparison, nested generic descent, large
    // ValueTuple flattening (TRest), explicit-vs-default element name comparison, and
    // the mismatch rejection path.
    // ---------------------------------------------------------------------

    [Fact]
    public void InheritedArrayTupleParameters_WithMatchingNames_Deduplicate()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Tuple.Array
            {
                public interface ILeft  { int Echo((int A, int B)[] values); }
                public interface IRight { int Echo((int A, int B)[] values); }

                [RpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var (final, runResult) = Run(source);
        AssertCompiles(final);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS003");
        GetFooProxy(runResult).Should().Contain("Echo((int A, int B)[] values)");
    }

    [Fact]
    public void InheritedArrayTupleParameters_WithDifferentNames_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;

            namespace Tuple.ArrayMismatch
            {
                public interface ILeft  { int Echo((int A, int B)[] values); }
                public interface IRight { int Echo((int X, int Y)[] values); }

                [RpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var (_, runResult) = Run(source);
        AssertRejectedForTupleNames(runResult);
    }

    [Fact]
    public void InheritedNestedGenericTupleReturns_WithDifferentNames_RejectService()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Collections.Generic;

            namespace Tuple.NestedGeneric
            {
                public interface ILeft  { Dictionary<string, (int A, int B)> Echo(); }
                public interface IRight { Dictionary<string, (int X, int Y)> Echo(); }

                [RpcService]
                public interface IFoo : ILeft, IRight { }
            }
            """;

        var (_, runResult) = Run(source);
        AssertRejectedForTupleNames(runResult);
    }

}
