using System.Text;

namespace AgentQueue;

internal sealed class QueueRenderer
{
    private readonly AgentQueuePaths paths;

    public QueueRenderer(AgentQueuePaths paths)
    {
        this.paths = paths;
    }

    public string QueuePathFor(AgentArea area) =>
        paths.QueueFileFor(area);

    public void RenderAll(IReadOnlyList<Finding> findings)
    {
        foreach (AgentArea area in AgentQueueCatalog.Areas)
        {
            RenderArea(area, findings);
        }
    }

    public void RenderArea(AgentArea area, IReadOnlyList<Finding> findings) =>
        AtomicFile.WriteAllText(paths.QueueFileFor(area), Generate(area, findings));

    public string Generate(AgentArea area, IEnumerable<Finding> findings)
    {
        List<Finding> areaFindings = FindingSort.Order(findings)
            .Where(finding => finding.Area == area.Name)
            .ToList();

        StringBuilder builder = new();
        builder.AppendLine(AgentQueueCatalog.QueueGeneratedWarning);
        builder.AppendLine();
        builder.AppendLine("# " + area.DisplayName + " Queue");
        builder.AppendLine();
        AppendSection(builder, "Open", "[ ]", areaFindings.Where(finding => finding.Status == "open"));
        AppendSection(builder, "Claimed", "[>]", areaFindings.Where(finding => finding.Status == "claimed"));
        AppendSection(
            builder,
            "Fixed pending verification",
            "[~]",
            areaFindings.Where(finding => finding.Status == "fixed_pending_verification"));
        AppendSection(builder, "Verified", "[x]", areaFindings.Where(finding => finding.Status == "verified"));
        AppendSection(
            builder,
            "Rejected / duplicate / obsolete",
            "[-]",
            areaFindings.Where(finding => AgentQueueCatalog.IsFinalStatus(finding.Status) && finding.Status != "verified"));
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private void AppendSection(StringBuilder builder, string title, string checkbox, IEnumerable<Finding> findings)
    {
        builder.AppendLine("## " + title);
        builder.AppendLine();
        foreach (Finding finding in findings)
        {
            builder.AppendLine($"- {checkbox} `{finding.Id}` {finding.Priority} - {finding.Title}");
            AppendDetails(builder, finding);
        }

        builder.AppendLine();
    }

    private void AppendDetails(StringBuilder builder, Finding finding)
    {
        if (finding.FilePath is not null)
        {
            builder.AppendLine("  - File: `" + paths.ToDisplayPath(finding.FilePath) + "`");
        }

        if (finding.Status == "open")
        {
            builder.AppendLine("  - Dedup: `" + finding.DedupKey + "`");
        }
        else if (finding.Status == "claimed")
        {
            builder.AppendLine("  - Owner: `" + finding.Get("claimed_by") + "`");
            builder.AppendLine("  - Branch: `" + finding.Get("claim_branch") + "`");
        }
        else if (finding.Status == "fixed_pending_verification")
        {
            builder.AppendLine("  - Fixed by: `" + finding.Get("fixed_by") + "`");
            builder.AppendLine("  - Commit: `" + finding.Get("fixed_commit") + "`");
        }
        else if (finding.Status == "duplicate")
        {
            builder.AppendLine("  - Duplicate of: `" + finding.Get("duplicate_of") + "`");
        }
    }
}
