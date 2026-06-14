namespace AgentQueue;

internal sealed class AgentQueueException : Exception
{
    public AgentQueueException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
