using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Hosting.Execution;

internal static class SandboxAuditEventSequence
{
    public static IReadOnlyList<SandboxAuditEvent> ToSequencedArray(this IEnumerable<SandboxAuditEvent> events)
    {
        if (events is IReadOnlyList<SandboxAuditEvent> list && HasContiguousSequenceNumbers(list))
        {
            if (list.Count == 0)
            {
                return InMemoryAuditSink.EmptyEventSnapshot;
            }

            return list is OwnedAuditEventSnapshot owned ? owned : new OwnedAuditEventSnapshot(list.ToArray());
        }

        var sink = new InMemoryAuditSink();
        foreach (var auditEvent in events)
        {
            sink.Write(auditEvent);
        }

        return sink.OwnedEventSnapshot();
    }

    private static bool HasContiguousSequenceNumbers(IReadOnlyList<SandboxAuditEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].SequenceNumber != i + 1L)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Wraps the sink's already-fresh event array in a read-only collection without copying
    /// it again, producing an owned immutable snapshot that result construction can adopt.
    /// </summary>
    internal static IReadOnlyList<SandboxAuditEvent> OwnedEventSnapshot(this InMemoryAuditSink sink)
        => sink.SnapshotEvents();
}
