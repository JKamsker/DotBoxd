namespace AgentQueue.Infrastructure;

internal sealed class CommandLine
{
    private readonly Dictionary<string, List<string>> options;

    private CommandLine(
        string command,
        IReadOnlyList<string> positionals,
        Dictionary<string, List<string>> options)
    {
        Command = command;
        Positionals = positionals;
        this.options = options;
    }

    public string Command { get; }

    public IReadOnlyList<string> Positionals { get; }

    public bool IsHelp => CommandEquals("help") || HasOption("help");

    public TimeSpan LockTimeout
    {
        get
        {
            string? value = GetOption("lock-timeout");
            return value is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(ParsePositiveInt(value));
        }
    }

    public static CommandLine Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new CommandLine("help", [], new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));
        }

        string command = args[0];
        List<string> positionals = [];
        Dictionary<string, List<string>> options = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 1; index < args.Length; index++)
        {
            string current = args[index];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(current);
                continue;
            }

            string option = current[2..];
            string value = "true";
            int equalsIndex = option.IndexOf('=');
            if (equalsIndex >= 0)
            {
                value = option[(equalsIndex + 1)..];
                option = option[..equalsIndex];
            }
            else if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++index];
            }

            if (!options.TryGetValue(option, out List<string>? values))
            {
                values = [];
                options.Add(option, values);
            }

            values.Add(value);
        }

        return new CommandLine(command, positionals, options);
    }

    public bool CommandEquals(string value) =>
        string.Equals(Command, value, StringComparison.OrdinalIgnoreCase);

    public bool HasOption(string name) =>
        options.ContainsKey(name);

    public string? GetOption(string name) =>
        options.TryGetValue(name, out List<string>? values) ? values[^1] : null;

    private static int ParsePositiveInt(string value)
    {
        if (!int.TryParse(value, out int result) || result <= 0)
        {
            throw new AgentQueueException($"Expected a positive integer, got '{value}'.", ExitCodes.UserError);
        }

        return result;
    }
}
