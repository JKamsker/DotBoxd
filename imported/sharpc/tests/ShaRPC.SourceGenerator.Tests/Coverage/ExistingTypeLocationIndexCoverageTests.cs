using System.Collections.Immutable;
using System.Threading;
using ShaRPC.SourceGenerator;
using Xunit;

namespace ShaRPC.SourceGenerator.Tests.Cov;

// Round-2 coverage: ExistingTypeLocationIndex.Find (binary search miss path, line 63) and
// CompareLocations (lines 66-101) tie-breaking across every field. We feed declarations sharing
// the SAME key but DIFFERENT DiagnosticLocations so the sort comparator must walk each tie-break
// field in turn (FilePath, Start, Length, StartLine, StartCharacter, EndLine, EndCharacter), then
// assert dedup keeps exactly one entry per key and that the surviving location is the smallest one.

public sealed class ExistingTypeLocationIndexCoverageTests
{
    private static ExistingTypeKey Key(string name) => new("Ns", name, 0);

    // Fully-populated DiagnosticLocation; helpers below vary a single field so a specific
    // CompareLocations tie-break branch decides the order.
    private static DiagnosticLocation Loc(
        string filePath = "F.cs",
        int start = 10,
        int length = 5,
        int startLine = 1,
        int startCharacter = 1,
        int endLine = 1,
        int endCharacter = 6) =>
        new(filePath, start, length, startLine, startCharacter, endLine, endCharacter);

    private static ExistingTypeDeclaration Decl(ExistingTypeKey key, DiagnosticLocation location) =>
        new(key, location);

    [Fact]
    public void Find_Hit_ReturnsLocation_Miss_ReturnsDefault()
    {
        var index = ExistingTypeLocationIndex.Create(
            ImmutableArray.Create(
                Decl(Key("AProxy"), Loc(filePath: "A.cs")),
                Decl(Key("BProxy"), Loc(filePath: "B.cs")),
                Decl(Key("CProxy"), Loc(filePath: "C.cs"))),
            CancellationToken.None);

        var hit = index.Find(Key("BProxy"), CancellationToken.None);
        var miss = index.Find(Key("ZProxy"), CancellationToken.None); // line 63: default on miss

        Assert.Equal("B.cs", hit.FilePath);
        Assert.Equal(default(DiagnosticLocation), miss);
    }

    [Fact]
    public void CompareLocations_TieBreaksOnFilePath_KeepsSmallestPath()
    {
        var key = Key("FooProxy");
        var index = ExistingTypeLocationIndex.Create(
            ImmutableArray.Create(
                Decl(key, Loc(filePath: "Z.cs")),
                Decl(key, Loc(filePath: "A.cs"))),
            CancellationToken.None);

        // Same key => deduped to one; the kept location is the ordinal-smallest FilePath. (lines 68-72)
        Assert.Equal(1, index.Types.Count);
        Assert.Equal("A.cs", index.Find(key, CancellationToken.None).FilePath);
    }

    [Fact]
    public void CompareLocations_TieBreaksOnStart_WhenFilePathEqual()
    {
        var key = Key("FooProxy");
        var index = ExistingTypeLocationIndex.Create(
            ImmutableArray.Create(
                Decl(key, Loc(start: 30)),
                Decl(key, Loc(start: 5))),
            CancellationToken.None);

        // Equal FilePath => fall through to Start comparison (lines 74-77).
        Assert.Equal(1, index.Types.Count);
        Assert.Equal(5, index.Find(key, CancellationToken.None).Start);
    }

    [Fact]
    public void CompareLocations_TieBreaksOnLength_WhenFilePathAndStartEqual()
    {
        var key = Key("FooProxy");
        var index = ExistingTypeLocationIndex.Create(
            ImmutableArray.Create(
                Decl(key, Loc(length: 9)),
                Decl(key, Loc(length: 2))),
            CancellationToken.None);

        // Equal FilePath+Start => Length comparison (lines 80-83).
        Assert.Equal(1, index.Types.Count);
        Assert.Equal(2, index.Find(key, CancellationToken.None).Length);
    }

    [Fact]
    public void CompareLocations_TieBreaksOnStartLine_WhenPriorFieldsEqual()
    {
        var key = Key("FooProxy");
        var index = ExistingTypeLocationIndex.Create(
            ImmutableArray.Create(
                Decl(key, Loc(startLine: 8)),
                Decl(key, Loc(startLine: 3))),
            CancellationToken.None);

        // Equal FilePath+Start+Length => StartLine comparison (lines 86-89).
        Assert.Equal(1, index.Types.Count);
        Assert.Equal(3, index.Find(key, CancellationToken.None).StartLine);
    }

    [Fact]
    public void CompareLocations_TieBreaksOnStartCharacter_WhenPriorFieldsEqual()
    {
        var key = Key("FooProxy");
        var index = ExistingTypeLocationIndex.Create(
            ImmutableArray.Create(
                Decl(key, Loc(startCharacter: 7)),
                Decl(key, Loc(startCharacter: 2))),
            CancellationToken.None);

        // Equal through StartLine => StartCharacter comparison (lines 92-95).
        Assert.Equal(1, index.Types.Count);
        Assert.Equal(2, index.Find(key, CancellationToken.None).StartCharacter);
    }

    [Fact]
    public void CompareLocations_TieBreaksOnEndLine_WhenPriorFieldsEqual()
    {
        var key = Key("FooProxy");
        var index = ExistingTypeLocationIndex.Create(
            ImmutableArray.Create(
                Decl(key, Loc(endLine: 9, endCharacter: 1)),
                Decl(key, Loc(endLine: 4, endCharacter: 1))),
            CancellationToken.None);

        // Equal through StartCharacter => EndLine comparison (lines 98-100).
        Assert.Equal(1, index.Types.Count);
        Assert.Equal(4, index.Find(key, CancellationToken.None).EndLine);
    }

    [Fact]
    public void CompareLocations_TieBreaksOnEndCharacter_AsFinalField()
    {
        var key = Key("FooProxy");
        var index = ExistingTypeLocationIndex.Create(
            ImmutableArray.Create(
                Decl(key, Loc(endCharacter: 12)),
                Decl(key, Loc(endCharacter: 6))),
            CancellationToken.None);

        // All prior fields equal => EndCharacter is the final tie-break (line 101).
        Assert.Equal(1, index.Types.Count);
        Assert.Equal(6, index.Find(key, CancellationToken.None).EndCharacter);
    }

    [Fact]
    public void Create_DistinctKeys_AreAllRetainedAndFindable()
    {
        var index = ExistingTypeLocationIndex.Create(
            ImmutableArray.Create(
                Decl(Key("CProxy"), Loc(filePath: "C.cs")),
                Decl(Key("AProxy"), Loc(filePath: "A.cs")),
                Decl(Key("BProxy"), Loc(filePath: "B.cs"))),
            CancellationToken.None);

        Assert.Equal(3, index.Types.Count);
        Assert.Equal("A.cs", index.Find(Key("AProxy"), CancellationToken.None).FilePath);
        Assert.Equal("B.cs", index.Find(Key("BProxy"), CancellationToken.None).FilePath);
        Assert.Equal("C.cs", index.Find(Key("CProxy"), CancellationToken.None).FilePath);
    }
}
