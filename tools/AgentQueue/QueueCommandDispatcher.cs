namespace AgentQueue;

internal sealed class QueueCommandDispatcher
{
    private readonly QueueMaintenanceCommands maintenance;
    private readonly QueueMutationCommands mutations;
    private readonly QueueQueryCommands queries;

    public QueueCommandDispatcher(
        FindingRepository repository,
        QueueRenderer renderer,
        QueueDoctor doctor,
        ISystemClock clock,
        TextWriter output)
    {
        maintenance = new QueueMaintenanceCommands(repository, renderer, doctor, output);
        mutations = new QueueMutationCommands(repository, renderer, clock, output);
        queries = new QueueQueryCommands(repository, output);
    }

    public int Run(CommandLine commandLine) =>
        commandLine.Command.ToLowerInvariant() switch
        {
            "init" => maintenance.Init(commandLine),
            "render" => maintenance.Render(commandLine),
            "doctor" => maintenance.Doctor(),
            "append" => mutations.Append(commandLine),
            "claim" => mutations.Claim(commandLine),
            "release" => mutations.Release(commandLine),
            "fix" => mutations.Fix(commandLine),
            "verify" => mutations.Verify(commandLine),
            "reopen" => mutations.Reopen(commandLine),
            "reject" => mutations.Finalize(commandLine, "rejected"),
            "duplicate" => mutations.Finalize(commandLine, "duplicate"),
            "obsolete" => mutations.Finalize(commandLine, "obsolete"),
            "note" => mutations.Note(commandLine),
            "list" => queries.List(commandLine),
            "next" => queries.Next(commandLine),
            _ => throw new AgentQueueException($"Unknown command '{commandLine.Command}'.", ExitCodes.UserError)
        };

    public static void WriteHelp(TextWriter output)
    {
        output.WriteLine("agentq commands:");
        output.WriteLine("  init, append, list, next, claim, release, fix, verify");
        output.WriteLine("  reopen, reject, duplicate, obsolete, note, render, doctor");
    }
}
