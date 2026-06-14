namespace AgentQueue.Infrastructure;

internal static class CommandLineExtensions
{
    public static string RequireOption(this CommandLine commandLine, string name)
    {
        string? value = commandLine.GetOption(name);
        if (string.IsNullOrWhiteSpace(value) || value == "true")
        {
            throw new AgentQueueException($"Missing required option --{name}.", ExitCodes.UserError);
        }

        return value.Trim();
    }

    public static string RequireArgument(this CommandLine commandLine, string commandName)
    {
        if (commandLine.Positionals.Count == 0)
        {
            throw new AgentQueueException($"{commandName} requires a finding ID.", ExitCodes.UserError);
        }

        return commandLine.Positionals[0];
    }
}
