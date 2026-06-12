namespace AgentQueue;

internal sealed class QueueMaintenanceCommands
{
    private readonly FindingRepository repository;
    private readonly QueueRenderer renderer;
    private readonly QueueDoctor doctor;
    private readonly TextWriter output;

    public QueueMaintenanceCommands(
        FindingRepository repository,
        QueueRenderer renderer,
        QueueDoctor doctor,
        TextWriter output)
    {
        this.repository = repository;
        this.renderer = renderer;
        this.doctor = doctor;
        this.output = output;
    }

    public int Init(CommandLine commandLine)
    {
        repository.EnsureLayout(commandLine.HasOption("force"));
        renderer.RenderAll(repository.LoadAll());
        output.WriteLine("initialized docs/agent-loop");
        return ExitCodes.Success;
    }

    public int Render(CommandLine commandLine)
    {
        IReadOnlyList<Finding> findings = repository.LoadAll();
        string? areaName = commandLine.GetOption("area");
        AgentArea[] areas = areaName is null
            ? AgentQueueCatalog.Areas
            : [AgentQueueCatalog.RequireArea(areaName)];

        if (commandLine.HasOption("check"))
        {
            return CheckRender(areas, findings);
        }

        foreach (AgentArea area in areas)
        {
            renderer.RenderArea(area, findings);
        }

        output.WriteLine("rendered queues");
        return ExitCodes.Success;
    }

    public int Doctor()
    {
        IReadOnlyList<string> errors = doctor.Validate();
        if (errors.Count == 0)
        {
            output.WriteLine("doctor ok");
            return ExitCodes.Success;
        }

        foreach (string error in errors)
        {
            output.WriteLine("ERROR " + error);
        }

        return ExitCodes.ValidationError;
    }

    private int CheckRender(IReadOnlyList<AgentArea> areas, IReadOnlyList<Finding> findings)
    {
        List<string> stale = [];
        foreach (AgentArea area in areas)
        {
            string path = renderer.QueuePathFor(area);
            string expected = renderer.Generate(area, findings).Replace("\r\n", "\n");
            string actual = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : string.Empty;
            if (actual != expected)
            {
                stale.Add(path);
            }
        }

        if (stale.Count == 0)
        {
            output.WriteLine("render check ok");
            return ExitCodes.Success;
        }

        foreach (string path in stale)
        {
            output.WriteLine("STALE " + path);
        }

        return ExitCodes.ValidationError;
    }
}
