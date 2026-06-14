namespace AgentQueue;

internal sealed class AgentQueuePaths
{
    public AgentQueuePaths(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        AgentLoopDirectory = Path.Combine(RootDirectory, "docs", "agent-loop");
        FindingsDirectory = Path.Combine(AgentLoopDirectory, "findings");
        EventsDirectory = Path.Combine(AgentLoopDirectory, "events");
        QueuesDirectory = Path.Combine(AgentLoopDirectory, "queues");
        ActiveDirectory = Path.Combine(AgentLoopDirectory, "active");
        LockFile = Path.Combine(AgentLoopDirectory, ".agentq.lock");
    }

    public string RootDirectory { get; }

    public string AgentLoopDirectory { get; }

    public string FindingsDirectory { get; }

    public string EventsDirectory { get; }

    public string QueuesDirectory { get; }

    public string ActiveDirectory { get; }

    public string LockFile { get; }

    public static string DiscoverRoot(string startDirectory)
    {
        DirectoryInfo? current = new(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            string gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new AgentQueueException("Could not find a repository root with a .git entry.", ExitCodes.UserError);
    }

    public string QueueFileFor(AgentArea area) =>
        Path.Combine(QueuesDirectory, area.QueueFile);

    public string EventFileFor(string id) =>
        Path.Combine(EventsDirectory, id + ".jsonl");

    public string ToDisplayPath(string path) =>
        Path.GetRelativePath(RootDirectory, path).Replace('\\', '/');
}
