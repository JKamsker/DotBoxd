namespace AgentQueue;

internal interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

internal sealed class SystemClock : ISystemClock
{
    public static readonly SystemClock Instance = new();

    private SystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
