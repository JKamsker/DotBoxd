namespace AgentQueue;

internal static class FindingSort
{
    public static IOrderedEnumerable<Finding> Order(IEnumerable<Finding> findings) =>
        findings
            .OrderBy(finding => AgentQueueCatalog.PriorityRank(finding.Priority))
            .ThenBy(finding => AgentQueueCatalog.StatusRank(finding.Status))
            .ThenBy(finding => finding.CreatedAt)
            .ThenBy(finding => finding.Id, StringComparer.Ordinal);
}
