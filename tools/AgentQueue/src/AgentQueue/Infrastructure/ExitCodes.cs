namespace AgentQueue.Infrastructure;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int UserError = 1;
    public const int ValidationError = 2;
    public const int DuplicateFinding = 3;
    public const int InvalidTransition = 4;
    public const int LockTimeout = 5;
    public const int InternalError = 10;
}
