namespace CodeEnforcer;

internal sealed class CodeEnforcerException : Exception
{
    public CodeEnforcerException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
