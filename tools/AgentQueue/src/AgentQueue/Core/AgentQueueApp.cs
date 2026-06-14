namespace AgentQueue;

internal sealed class AgentQueueApp
{
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly ISystemClock clock;

    public AgentQueueApp(TextWriter output, TextWriter error, ISystemClock clock)
    {
        this.output = output;
        this.error = error;
        this.clock = clock;
    }

    public int Run(string[] args, string currentDirectory)
    {
        try
        {
            CommandLine commandLine = CommandLine.Parse(args);
            if (commandLine.IsHelp)
            {
                QueueCommandDispatcher.WriteHelp(output);
                return ExitCodes.Success;
            }

            string root = AgentQueuePaths.DiscoverRoot(currentDirectory);
            AgentQueuePaths paths = new(root);
            FindingRepository repository = new(paths);
            QueueRenderer renderer = new(paths);
            QueueDoctor doctor = new(paths, repository, renderer);
            QueueCommandDispatcher dispatcher = new(repository, renderer, doctor, clock, output);

            using AgentQueueLock? queueLock = ShouldLock(commandLine)
                ? AgentQueueLock.Acquire(paths, commandLine.Command, commandLine.LockTimeout)
                : null;

            return dispatcher.Run(commandLine);
        }
        catch (AgentQueueException ex)
        {
            error.WriteLine("error: " + ex.Message);
            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            error.WriteLine("error: " + ex.Message);
            return ExitCodes.InternalError;
        }
    }

    private static bool ShouldLock(CommandLine commandLine)
    {
        if (commandLine.CommandEquals("list") || commandLine.CommandEquals("next") ||
            commandLine.CommandEquals("doctor"))
        {
            return false;
        }

        return !commandLine.CommandEquals("render") || !commandLine.HasOption("check");
    }
}
