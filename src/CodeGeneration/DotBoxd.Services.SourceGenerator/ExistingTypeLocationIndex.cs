using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace DotBoxd.Services.SourceGenerator;

internal sealed record ExistingTypeLocationIndex(EquatableArray<ExistingTypeDeclaration> Types)
{
    public static ExistingTypeLocationIndex Create(
        ImmutableArray<ExistingTypeDeclaration> declarations,
        CancellationToken ct)
    {
        var ordered = new List<ExistingTypeDeclaration>(declarations);
        ordered.Sort((left, right) =>
        {
            ct.ThrowIfCancellationRequested();

            var key = ExistingTypeKeyComparer.Instance.Compare(left.Key, right.Key);
            return key != 0
                ? key
                : CompareLocations(left.Location, right.Location);
        });

        var unique = new List<ExistingTypeDeclaration>(ordered.Count);
        foreach (var type in ordered)
        {
            ct.ThrowIfCancellationRequested();
            if (unique.Count == 0 ||
                ExistingTypeKeyComparer.Instance.Compare(unique[unique.Count - 1].Key, type.Key) != 0)
            {
                unique.Add(type);
            }
        }

        return new ExistingTypeLocationIndex(unique.ToEquatableArray());
    }

    public DiagnosticLocation Find(ExistingTypeKey target, CancellationToken ct)
    {
        var low = 0;
        var high = Types.Count - 1;
        while (low <= high)
        {
            ct.ThrowIfCancellationRequested();

            var mid = low + ((high - low) / 2);
            var comparison = ExistingTypeKeyComparer.Instance.Compare(Types[mid].Key, target);
            if (comparison == 0)
            {
                return Types[mid].Location;
            }

            if (comparison < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return default;
    }

    private static int CompareLocations(DiagnosticLocation left, DiagnosticLocation right)
    {
        var filePath = string.Compare(left.FilePath, right.FilePath, System.StringComparison.Ordinal);
        if (filePath != 0)
        {
            return filePath;
        }

        var start = left.Start.CompareTo(right.Start);
        if (start != 0)
        {
            return start;
        }

        var length = left.Length.CompareTo(right.Length);
        if (length != 0)
        {
            return length;
        }

        var startLine = left.StartLine.CompareTo(right.StartLine);
        if (startLine != 0)
        {
            return startLine;
        }

        var startCharacter = left.StartCharacter.CompareTo(right.StartCharacter);
        if (startCharacter != 0)
        {
            return startCharacter;
        }

        var endLine = left.EndLine.CompareTo(right.EndLine);
        return endLine != 0
            ? endLine
            : left.EndCharacter.CompareTo(right.EndCharacter);
    }
}

internal readonly record struct ExistingTypeDeclaration(
    ExistingTypeKey Key,
    DiagnosticLocation Location);
