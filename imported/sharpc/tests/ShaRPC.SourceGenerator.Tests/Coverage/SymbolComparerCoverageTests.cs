using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.SourceGenerator;
using ShaRPC.SourceGenerator.Tests;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

// Round-2 coverage: DiagnosticLocationFactory.FromSymbol fallback (lines 15,17) and
// TupleElementNameComparer.HasSameElementNames tuple/generic walking (lines 66-67, 83, 94-95,
// 139-150, 153-154). All symbols are extracted from a real in-memory CSharpCompilation built via
// GeneratorTestHelper, so we assert observable true/false comparison results — no reflection.

public sealed class DiagnosticLocationFactoryCoverageTests
{
    [Fact]
    public void FromSymbol_MetadataSymbolWithNoInSourceLocation_ReturnsDefault()
    {
        var compilation = GeneratorTestHelper.CreateCompilation("namespace Probe { class C { } }");
        var int32 = compilation.GetSpecialType(SpecialType.System_Int32);

        // System.Int32 lives in metadata: every Location.IsInSource is false, so the loop exhausts
        // and the method returns default (lines 15,17).
        var location = DiagnosticLocationFactory.FromSymbol(int32);

        Assert.Equal(default(DiagnosticLocation), location);
        Assert.Equal(0, location.Length);
    }

    [Fact]
    public void FromSymbol_InSourceSymbol_ReturnsItsSourceLocation()
    {
        const string source = """
            namespace Probe
            {
                class Marker { }
            }
            """;
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var marker = compilation.GetTypeByMetadataName("Probe.Marker");
        Assert.NotNull(marker);

        // In-source symbol hits the IsInSource branch (line 11/13) and yields a real location.
        var location = DiagnosticLocationFactory.FromSymbol(marker!);

        Assert.NotEqual(default(DiagnosticLocation), location);
        Assert.True(location.Length > 0);
        Assert.Equal("Marker".Length, location.Length);
    }
}

public sealed class TupleElementNameComparerCoverageTests
{
    // One interface whose methods deliberately exercise each comparison branch. Method names are
    // unique so we can pick exact IMethodSymbols out of the compiled type.
    //
    // Note on the TRest path (AddTupleElements lines 139-150): a *syntactic* 9-tuple like
    // "(int,...,int)" surfaces as IsTupleType=true and flattens via TupleElements, NOT TRest.
    // To force the TRest recursion the right-hand operand uses the raw
    // System.ValueTuple<...,System.ValueTuple<int,int>> form (IsTupleType=false, IsValueTuple=true),
    // which is exactly how the existing InheritedTupleElementNameTests reach this code.
    private const string Source = """
        using System.Collections.Generic;

        namespace TupleProbe
        {
            // Generic type nested inside another generic type, so a comparison recurses into the
            // ContainingType (line 83): Box<int>.Item<string>.
            public sealed class Box<TOuter>
            {
                public sealed class Item<TInner> { }
            }

            public interface IShapes
            {
                // Different generic type-argument counts: Dictionary<,> (2) vs List<> (1).
                System.Collections.Generic.Dictionary<int, string> DictTwoArgs();
                System.Collections.Generic.List<int> ListOneArg();

                // Same generic head, different tuple-element counts inside: 2 vs 3 fields.
                List<(int A, int B)> TwoFieldTuple();
                List<(int A, int B, int C)> ThreeFieldTuple();

                // Named vs differently-named tuple fields of equal length.
                (int A, int B) NamedAB();
                (int X, int Y) NamedXY();

                // Identical named tuples (positive equality).
                (int A, int B) NamedABClone();

                // Syntactic 9-tuple (IsTupleType=true) vs raw-ValueTuple 9-tuple (TRest form).
                // Comparing these two forces AddTupleElements to recurse through the 8th TRest slot
                // on the raw-form operand to flatten all 9 fields (lines 139-150).
                (int, int, int, int, int, int, int, int, int) NineTupleSyntactic();
                System.ValueTuple<int, int, int, int, int, int, int, System.ValueTuple<int, int>> NineTupleRaw();

                // Non-tuple generic argument inside a generic => the i!=7 / non-rest fallback path.
                List<List<int>> NestedGeneric();

                // Nested generic types: ContainingType recursion (line 83). Identical pair is equal.
                Box<int>.Item<string> NestedTypeA();
                Box<int>.Item<string> NestedTypeAClone();
            }
        }
        """;

    private static Dictionary<string, IMethodSymbol> LoadMethods()
    {
        var compilation = GeneratorTestHelper.CreateCompilation(Source);
        var type = compilation.GetTypeByMetadataName("TupleProbe.IShapes");
        Assert.NotNull(type);
        return type!.GetMembers()
            .OfType<IMethodSymbol>()
            .ToDictionary(m => m.Name);
    }

    private static bool Compare(IMethodSymbol left, IMethodSymbol right) =>
        TupleElementNameComparer.HasSameElementNames(left, right, CancellationToken.None);

    [Fact]
    public void DifferentGenericTypeArgumentCounts_AreNotEqual()
    {
        var m = LoadMethods();

        // Dictionary<int,string> (2 args) vs List<int> (1 arg) => length mismatch (lines 66-67).
        Assert.False(Compare(m["DictTwoArgs"], m["ListOneArg"]));
    }

    [Fact]
    public void DifferentTupleFieldCounts_AreNotEqual()
    {
        var m = LoadMethods();

        // List<(int A,int B)> vs List<(int A,int B,int C)> => tuple element-count mismatch (lines 94-95).
        Assert.False(Compare(m["TwoFieldTuple"], m["ThreeFieldTuple"]));
    }

    [Fact]
    public void DifferentTupleElementNames_AreNotEqual()
    {
        var m = LoadMethods();

        // (int A,int B) vs (int X,int Y): equal arity, different field names.
        Assert.False(Compare(m["NamedAB"], m["NamedXY"]));
    }

    [Fact]
    public void IdenticalNamedTuples_AreEqual()
    {
        var m = LoadMethods();

        // (int A,int B) vs (int A,int B): positive path through the tuple field loop and
        // the containing-type null/null branch (lines 78-83 area).
        Assert.True(Compare(m["NamedAB"], m["NamedABClone"]));
    }

    [Fact]
    public void SyntacticNineTuple_VsRawValueTupleWithTRest_AreEqual()
    {
        var m = LoadMethods();

        // The raw-ValueTuple operand is ValueTuple<..7.., ValueTuple<int,int>>. AddTupleElements must
        // recurse into the 8th TRest slot to flatten all 9 fields (lines 139-150) so it lines up with
        // the syntactic 9-tuple. Both flatten to 9 unnamed fields => equal.
        Assert.True(Compare(m["NineTupleSyntactic"], m["NineTupleRaw"]));
    }

    [Fact]
    public void NestedNonTupleGenericArguments_CompareByStructure()
    {
        var m = LoadMethods();

        // List<List<int>>: the inner List<int> is a non-tuple generic argument, exercising the
        // generic type-argument recursion (lines 70-76) and the non-rest add path (lines 153-154)
        // rather than tuple flattening; comparing the method with itself is a positive structural match.
        Assert.True(Compare(m["NestedGeneric"], m["NestedGeneric"]));
    }

    [Fact]
    public void NestedGenericTypes_RecurseThroughContainingType_AndAreEqual()
    {
        var m = LoadMethods();

        // Box<int>.Item<string>: a generic type nested in a generic type. After matching the type
        // arguments, the comparer recurses into the (non-null) ContainingType pair (line 83).
        // Identical nested types compare equal.
        Assert.True(Compare(m["NestedTypeA"], m["NestedTypeAClone"]));
    }

    [Fact]
    public void NineTupleVsThreeFieldTuple_AreNotEqual()
    {
        var m = LoadMethods();

        // Flattened 9-field tuple vs a 3-field tuple => count mismatch (lines 94-95) after TRest
        // flattening, confirming the recursion actually produced 9 elements.
        Assert.False(Compare(m["NineTupleRaw"], m["ThreeFieldTuple"]));
    }
}
