namespace AgentQueue;

internal sealed class QueueQueryCommands
{
    private readonly FindingRepository repository;
    private readonly TextWriter output;

    public QueueQueryCommands(FindingRepository repository, TextWriter output)
    {
        this.repository = repository;
        this.output = output;
    }

    public int List(CommandLine commandLine)
    {
        IReadOnlyList<Finding> findings = ApplyFilters(repository.LoadAll(), commandLine).ToArray();
        if (commandLine.HasOption("json"))
        {
            JsonOutput.Write(output, findings.Select(ToDto).ToArray());
            return ExitCodes.Success;
        }

        foreach (Finding finding in findings)
        {
            output.WriteLine($"{finding.Id} {finding.Status} {finding.Priority} {finding.Title}");
        }

        return ExitCodes.Success;
    }

    public int Next(CommandLine commandLine)
    {
        IEnumerable<Finding> findings = ApplyFilters(repository.LoadAll(), commandLine)
            .Where(finding => finding.Status == "open");
        Finding? next = findings.FirstOrDefault();
        if (next is null)
        {
            output.WriteLine("NO_OPEN_FINDING");
            return ExitCodes.UserError;
        }

        if (commandLine.HasOption("json"))
        {
            JsonOutput.Write(output, ToDto(next));
            return ExitCodes.Success;
        }

        output.WriteLine($"NEXT {next.Id}");
        output.WriteLine("file=" + next.FilePath);
        return ExitCodes.Success;
    }

    private static IEnumerable<Finding> ApplyFilters(IEnumerable<Finding> findings, CommandLine commandLine)
    {
        string? area = commandLine.GetOption("area");
        string? status = commandLine.GetOption("status");
        string? priority = commandLine.GetOption("priority");

        if (area is not null)
        {
            string areaName = AgentQueueCatalog.RequireArea(area).Name;
            findings = findings.Where(finding => finding.Area == areaName);
        }

        if (status is not null)
        {
            findings = findings.Where(finding => finding.Status == status);
        }

        if (priority is not null)
        {
            findings = findings.Where(finding => finding.Priority == priority);
        }

        return FindingSort.Order(findings);
    }

    private static object ToDto(Finding finding) => new
    {
        id = finding.Id,
        area = finding.Area,
        status = finding.Status,
        priority = finding.Priority,
        title = finding.Title,
        file = finding.FilePath
    };
}
