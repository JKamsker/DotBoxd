using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using ShaRPC.SourceGenerator;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

// Round-2 coverage: direct unit tests over the generator's internal value-model utilities.
// Targets the previously-uncovered lines in EquatableArray<T>, ServiceModelOrdering.Sort,
// and FinalRejectionMethodParameters.Build. Plain xUnit Assert (no FluentAssertions) to keep
// these tests trivially portable and assertion-explicit.

public sealed class EquatableArrayCoverageTests
{
    // A minimal IEquatable<T> value to exercise the generic constraint with a custom type and to
    // confirm content-based hashing/equality flows through T.GetHashCode / T.Equals.
    private readonly record struct Tag(string Value) : IEquatable<Tag>;

    private static EquatableArray<string> Content(params string[] items) =>
        new(ImmutableArray.Create(items));

    [Fact]
    public void ImplicitOperator_FromImmutableArray_WrapsArray()
    {
        ImmutableArray<string> source = ImmutableArray.Create("a", "b");

        EquatableArray<string> wrapped = source; // line 41: implicit operator

        Assert.Equal(2, wrapped.Count);
        Assert.Equal("a", wrapped[0]);
        Assert.Equal("b", wrapped[1]);
        Assert.True(source.SequenceEqual(wrapped.Array));
    }

    [Fact]
    public void Equals_DefaultVsDefault_IsTrue()
    {
        var left = default(EquatableArray<string>);
        var right = default(EquatableArray<string>);

        Assert.True(left.Equals(right)); // lines 45-47
    }

    [Fact]
    public void Equals_DefaultVsNonDefault_IsFalse()
    {
        var left = default(EquatableArray<string>);
        var right = EquatableArray<string>.Empty;

        Assert.False(left.Equals(right)); // lines 50-52
        Assert.False(right.Equals(left));
    }

    [Fact]
    public void Equals_SameContents_IsTrue_DifferentContents_AndLength_IsFalse()
    {
        Assert.True(Content("x", "y").Equals(Content("x", "y")));     // SequenceEqual true (line 55)
        Assert.False(Content("x", "y").Equals(Content("x", "z")));    // different element
        Assert.False(Content("x", "y").Equals(Content("x")));         // different length
    }

    [Fact]
    public void EqualsObject_TrueForEqual_FalseForUnequal_FalseForWrongType()
    {
        object equal = Content("p");
        object unequal = Content("q");
        object wrongType = "not an array";

        Assert.True(Content("p").Equals(equal));        // line 58 true branch
        Assert.False(Content("p").Equals(unequal));     // line 58 equal-but-different-content
        Assert.False(Content("p").Equals(wrongType));   // line 58 wrong-type branch
        Assert.False(Content("p").Equals((object?)null));
    }

    [Fact]
    public void GetHashCode_DefaultAndEmpty_AreZero()
    {
        Assert.Equal(0, default(EquatableArray<string>).GetHashCode()); // lines 67-69 (default)
        Assert.Equal(0, EquatableArray<string>.Empty.GetHashCode());    // lines 67-69 (empty)
    }

    [Fact]
    public void GetHashCode_ContentBased_IsStableAndEqualValuesHashEqual()
    {
        var a = Content("alpha", "beta");
        var b = Content("alpha", "beta");

        var hashA = a.GetHashCode(); // lines 74-82
        Assert.Equal(hashA, a.GetHashCode()); // stable across calls
        Assert.Equal(hashA, b.GetHashCode()); // equal values => equal hash
        Assert.NotEqual(0, hashA);             // non-empty content does not collapse to the empty sentinel
    }

    [Fact]
    public void GetHashCode_CustomEquatableElement_FlowsThroughElementHash()
    {
        var a = new EquatableArray<Tag>(ImmutableArray.Create(new Tag("one"), new Tag("two")));
        var b = new EquatableArray<Tag>(ImmutableArray.Create(new Tag("one"), new Tag("two")));

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void StructEnumerator_OverDefault_YieldsNothing()
    {
        var collected = new List<string>();
        foreach (var item in default(EquatableArray<string>)) // GetEnumerator over default (lines 84-88)
        {
            collected.Add(item);
        }

        Assert.Empty(collected);
    }

    [Fact]
    public void StructEnumerator_OverContent_YieldsAllInOrder()
    {
        var collected = new List<string>();
        foreach (var item in Content("a", "b", "c")) // GetEnumerator over content
        {
            collected.Add(item);
        }

        Assert.Equal(new[] { "a", "b", "c" }, collected);
    }

    [Fact]
    public void GenericIEnumerable_GetEnumerator_OverDefaultAndContent()
    {
        IEnumerable<string> overDefault = default(EquatableArray<string>);
        IEnumerable<string> overContent = Content("m", "n");

        Assert.Empty(overDefault.ToList());                       // IEnumerable<T>.GetEnumerator (lines 90-91), default path
        Assert.Equal(new[] { "m", "n" }, overContent.ToList());   // IEnumerable<T>.GetEnumerator content path
    }

    [Fact]
    public void NonGenericIEnumerable_GetEnumerator_OverDefaultAndContent()
    {
        IEnumerable overDefault = default(EquatableArray<string>);
        IEnumerable overContent = Content("u", "v");

        var defaultCount = 0;
        foreach (var _ in overDefault) // IEnumerable.GetEnumerator (lines 93-94), default path
        {
            defaultCount++;
        }

        var collected = new List<object?>();
        foreach (var item in overContent) // IEnumerable.GetEnumerator content path
        {
            collected.Add(item);
        }

        Assert.Equal(0, defaultCount);
        Assert.Equal(new object?[] { "u", "v" }, collected);
    }

    [Fact]
    public void OperatorEquals_And_NotEquals_ReflectValueEquality()
    {
        var a = Content("k");
        var b = Content("k");
        var c = Content("z");

        Assert.True(a == b);   // operator == (line 96)
        Assert.False(a == c);
        Assert.True(a != c);   // operator != (line 98)
        Assert.False(a != b);
    }

    [Fact]
    public void ToEquatableArray_FromIEnumerable_PreservesElements()
    {
        IEnumerable<string> source = new[] { "g", "h" };

        var result = source.ToEquatableArray(); // extension over IEnumerable<T> (line 105)

        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { "g", "h" }, result.ToArray());
    }

    [Fact]
    public void ToEquatableArray_FromImmutableArray_WrapsSameElements()
    {
        var source = ImmutableArray.Create("r", "s", "t");

        var result = source.ToEquatableArray(); // extension over ImmutableArray<T> (line 109)

        Assert.Equal(3, result.Count);
        Assert.True(source.SequenceEqual(result.Array));
    }

    [Fact]
    public void Indexer_Count_IsEmpty_ForDefaultEmptyAndContent()
    {
        var def = default(EquatableArray<string>);
        var empty = EquatableArray<string>.Empty;
        var content = Content("only");

        // default: Count short-circuits to 0 via IsDefault, IsEmpty true.
        Assert.Equal(0, def.Count);
        Assert.True(def.IsEmpty);

        // empty: backed by ImmutableArray.Empty, Count 0, IsEmpty true.
        Assert.Equal(0, empty.Count);
        Assert.True(empty.IsEmpty);

        // content: Count reflects length, IsEmpty false, indexer returns element.
        Assert.Equal(1, content.Count);
        Assert.False(content.IsEmpty);
        Assert.Equal("only", content[0]); // indexer this[int] (line 39)
    }
}

public sealed class ServiceModelOrderingCoverageTests
{
    private static ServiceModel Model(string @namespace, string interfaceName, string serviceName) =>
        new(@namespace, interfaceName, serviceName, EquatableArray<MethodModel>.Empty);

    [Fact]
    public void Sort_OrdersByNamespaceThenInterfaceThenService_Ordinal()
    {
        // Deliberately unsorted, spanning each tie-break dimension:
        //  - different namespace (Zeta vs Alpha)
        //  - same namespace, different interface (IBeta vs IAlpha within Alpha)
        //  - same namespace + interface, different service name (SvcB vs SvcA)
        var unsorted = ImmutableArray.Create(
            Model("Zeta", "IThing", "SvcA"),
            Model("Alpha", "IBeta", "SvcA"),
            Model("Alpha", "IAlpha", "SvcB"),
            Model("Alpha", "IAlpha", "SvcA"));

        var sorted = ServiceModelOrdering.Sort(unsorted, CancellationToken.None); // lines 13-32

        var order = sorted.ToArray()
            .Select(m => $"{m.Namespace}/{m.InterfaceName}/{m.ServiceName}")
            .ToArray();

        Assert.Equal(
            new[]
            {
                "Alpha/IAlpha/SvcA",
                "Alpha/IAlpha/SvcB",
                "Alpha/IBeta/SvcA",
                "Zeta/IThing/SvcA",
            },
            order);
    }

    [Fact]
    public void Sort_IsOrdinal_UppercaseSortsBeforeLowercase()
    {
        // Ordinal comparison: 'Z' (0x5A) < 'a' (0x61), so "Zeta" precedes "alpha".
        var unsorted = ImmutableArray.Create(
            Model("alpha", "IFoo", "S"),
            Model("Zeta", "IFoo", "S"));

        var sorted = ServiceModelOrdering.Sort(unsorted, CancellationToken.None);

        Assert.Equal("Zeta", sorted[0].Namespace);
        Assert.Equal("alpha", sorted[1].Namespace);
    }

    [Fact]
    public void Sort_EmptyInput_ReturnsEmpty()
    {
        var sorted = ServiceModelOrdering.Sort(ImmutableArray<ServiceModel>.Empty, CancellationToken.None);

        Assert.True(sorted.IsEmpty);
        Assert.Equal(0, sorted.Count);
    }
}

public sealed class FinalRejectionMethodParametersCoverageTests
{
    private static ParameterModel Normal(string name, string type) =>
        new(name, type, type);

    private static ParameterModel CancellationToken(string name) =>
        new(
            name,
            "global::System.Threading.CancellationToken",
            "global::System.Threading.CancellationToken",
            IsCancellationToken: true,
            HasDefaultValue: true);

    private static MethodModel Method(
        MethodReturnKind returnKind,
        bool hasCancellationToken,
        params ParameterModel[] parameters) =>
        new(
            Name: "Do",
            ExplicitImplementationType: "global::Test.IFoo",
            RpcName: "Do",
            ReturnKind: returnKind,
            DeclaredReturnType: "void",
            UnwrappedReturnType: null,
            ReturnRefKindKeyword: "",
            HasCancellationToken: hasCancellationToken,
            Parameters: new EquatableArray<ParameterModel>(ImmutableArray.Create(parameters)),
            AdditionalExplicitImplementationTypes: EquatableArray<string>.Empty);

    [Fact]
    public void Build_AsyncWithCancellationToken_ReturnsParametersUnchanged()
    {
        var original = Method(
            MethodReturnKind.TaskOf,
            hasCancellationToken: true,
            Normal("value", "int"),
            CancellationToken("token"));

        var result = FinalRejectionMethodParameters.Build(original, System.Threading.CancellationToken.None); // lines 10-12

        // Early-return path returns the SAME parameter sequence (token preserved as-is, no synthesis).
        Assert.True(original.Parameters.Equals(result));
        var names = result.ToArray().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "value", "token" }, names);
    }

    [Fact]
    public void Build_SyncMethod_FiltersExistingTokenAndAppendsSynthesizedCt()
    {
        // Sync method (not async) carrying both a normal parameter and a user cancellation token.
        var method = Method(
            MethodReturnKind.Sync,
            hasCancellationToken: true,
            Normal("count", "int"),
            CancellationToken("userToken"));

        var result = FinalRejectionMethodParameters.Build(method, System.Threading.CancellationToken.None); // lines 15-33

        var parameters = result.ToArray();
        // The user's IsCancellationToken parameter is dropped; a synthesized "ct" is appended.
        Assert.Equal(2, parameters.Length);
        Assert.Equal("count", parameters[0].Name);
        Assert.False(parameters[0].IsCancellationToken);

        var synthesized = parameters[1];
        Assert.Equal("ct", synthesized.Name);
        Assert.True(synthesized.IsCancellationToken);
        Assert.True(synthesized.HasDefaultValue);
        Assert.Equal("global::System.Threading.CancellationToken", synthesized.Type);
        Assert.Equal("global::System.Threading.CancellationToken", synthesized.SignatureType);

        // The original user token name no longer appears.
        Assert.DoesNotContain(parameters, p => p.Name == "userToken");
    }

    [Fact]
    public void Build_AsyncWithoutCancellationToken_AppendsSynthesizedCt()
    {
        // Async, but HasCancellationToken=false => does NOT take the early return; ct is synthesized.
        var method = Method(
            MethodReturnKind.Task,
            hasCancellationToken: false,
            Normal("name", "string"));

        var result = FinalRejectionMethodParameters.Build(method, System.Threading.CancellationToken.None);

        var parameters = result.ToArray();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("name", parameters[0].Name);
        Assert.Equal("ct", parameters[1].Name);
        Assert.True(parameters[1].IsCancellationToken);
    }

    [Fact]
    public void Build_SyncWithNoParameters_ProducesSingleSynthesizedCt()
    {
        var method = Method(MethodReturnKind.Void, hasCancellationToken: false);

        var result = FinalRejectionMethodParameters.Build(method, System.Threading.CancellationToken.None);

        var parameters = result.ToArray();
        Assert.Single(parameters);
        Assert.Equal("ct", parameters[0].Name);
        Assert.True(parameters[0].IsCancellationToken);
    }
}
