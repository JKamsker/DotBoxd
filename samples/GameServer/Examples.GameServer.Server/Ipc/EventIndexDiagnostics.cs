using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// The host reads DotBoxD's index metadata (<c>HookSubscriptionManifest.IndexedPredicates</c>, issue #47)
/// and compiles it into its own dispatch view. This collaborator just logs what the host could index when
/// a subscription kernel is installed — the kernel is still wired to the broad subscription pipeline, and
/// the host-side prefilter that turns this metadata into reduced fan-out lives in
/// <see cref="EventIndexMatcher{TEvent}"/> (see the EventIndex fan-out tests). Only predicates whose path
/// is an <see cref="EventIndexKeyAttribute"/> field are honored; the rest stay verified-IR only.
/// </summary>
internal static class EventIndexDiagnostics
{
    public static void Report(InstalledKernel kernel)
    {
        foreach (var subscription in kernel.Manifest.Subscriptions)
        {
            if (subscription.IndexedPredicates.Count == 0)
            {
                continue;
            }

            var simpleName = SimpleEventName(subscription.Event);
            var honored = HonoredPredicates(simpleName, subscription.IndexedPredicates);
            foreach (var predicate in honored)
            {
                // "subscription" for an equality bucket, "prefilter" for a range constraint — matching the
                // vocabulary in issue #47.
                var kind = predicate.Operator == IndexPredicateOperator.Equals ? "subscription" : "prefilter";
                Console.WriteLine(
                    $"[server] registered indexed {kind}: {simpleName} {predicate.Path} {OperatorSymbol(predicate.Operator)} {predicate.Value}");
            }

            if (honored.Count > 0 && !subscription.IndexCoversPredicate)
            {
                Console.WriteLine(
                    $"[server] indexed prefilter for {simpleName} is partial; verified IR stays the authority.");
            }
        }
    }

    private static IReadOnlyList<IndexedPredicate> HonoredPredicates(
        string? simpleEventName,
        IReadOnlyList<IndexedPredicate> predicates)
        => simpleEventName switch
        {
            "AttackEvent" => EventIndexMatcher<AttackEvent>.Create(predicates).HonoredPredicates,
            "MonsterAggroEvent" => EventIndexMatcher<MonsterAggroEvent>.Create(predicates).HonoredPredicates,
            _ => [],
        };

    private static string OperatorSymbol(IndexPredicateOperator op)
        => op switch
        {
            IndexPredicateOperator.Equals => "==",
            IndexPredicateOperator.NotEquals => "!=",
            IndexPredicateOperator.GreaterThan => ">",
            IndexPredicateOperator.GreaterThanOrEqual => ">=",
            IndexPredicateOperator.LessThan => "<",
            IndexPredicateOperator.LessThanOrEqual => "<=",
            _ => "?",
        };

    private static string? SimpleEventName(string? eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return eventName;
        }

        var lastDot = eventName!.LastIndexOf('.');
        return lastDot >= 0 && lastDot < eventName.Length - 1
            ? eventName[(lastDot + 1)..]
            : eventName;
    }
}
