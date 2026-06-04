using System.Linq;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

/// <summary>
/// Round 7 regression for <c>TupleElementNameComparer</c>. When a service inherits the same method from two
/// bases where one declares the return as a named tuple <c>(int x, string y)</c> and the other as the bare
/// <c>System.ValueTuple&lt;int, string&gt;</c>, both reduce to the same canonical signature key, so the
/// generator compares their tuple element names. <c>AddTupleElements</c> emits 'x'/'y' for the named form
/// but empty names for the bare form, so the unconditional name comparison fails and the generator wrongly
/// reports SHARPC003. A bare ValueTuple imposes no element-name contract and must be treated as compatible.
/// </summary>
public sealed class Round7_InheritedTupleNamedVsBareTests
{
    private const string Source = @"
using System.Threading.Tasks;
using ShaRPC.Core.Attributes;

namespace Bug.TupleSpelling
{
    public interface IBase1 { Task<(int x, string y)> FooAsync(); }
    public interface IBase2 { Task<System.ValueTuple<int, string>> FooAsync(); }

    [ShaRpcService]
    public interface IMyService : IBase1, IBase2 { }
}";

    [Fact]
    public void Generator_AcceptsNamedTupleAndBareValueTuple_SpellingsOfTheSameInheritedMethod()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source);
        var runResult = GeneratorTestHelper.CreateDriver().RunGenerators(compilation).GetRunResult();

        // The two spellings are wire-identical, so the inherited method must not be rejected.
        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "SHARPC003");

        // And the service is generated (proxy present) rather than skipped.
        Assert.Contains(runResult.GeneratedTrees, t => t.FilePath.Contains("ShaRpcProxy"));
    }
}
