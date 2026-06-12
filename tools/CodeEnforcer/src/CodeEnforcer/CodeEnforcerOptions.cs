namespace CodeEnforcer;

internal sealed class CodeEnforcerOptions
{
    public bool ShowHelp { get; private set; }

    public string? RootDirectory { get; private set; }

    public string? ConfigPath { get; private set; }

    public int? SoftLineLimit { get; private set; }

    public int? HardLineLimit { get; private set; }

    public int? MaxFilesPerFolder { get; private set; }

    public static CodeEnforcerOptions Parse(string[] args)
    {
        CodeEnforcerOptions options = new();
        for (int index = 0; index < args.Length; index++)
        {
            string current = args[index];
            switch (current)
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--root":
                    options.RootDirectory = RequireValue(args, ref index, current);
                    break;
                case "--config":
                    options.ConfigPath = RequireValue(args, ref index, current);
                    break;
                case "--soft-line-limit":
                    options.SoftLineLimit = ParsePositiveInt(RequireValue(args, ref index, current), current);
                    break;
                case "--hard-line-limit":
                    options.HardLineLimit = ParsePositiveInt(RequireValue(args, ref index, current), current);
                    break;
                case "--max-files-per-folder":
                    options.MaxFilesPerFolder = ParsePositiveInt(RequireValue(args, ref index, current), current);
                    break;
                default:
                    throw new CodeEnforcerException($"Unknown argument '{current}'.", ExitCodes.InputError);
            }
        }

        return options;
    }

    public void ApplyOverrides(CodeEnforcerConfig config)
    {
        config.SoftLineLimit = SoftLineLimit ?? config.SoftLineLimit;
        config.HardLineLimit = HardLineLimit ?? config.HardLineLimit;
        config.MaxFilesPerFolder = MaxFilesPerFolder ?? config.MaxFilesPerFolder;
        config.Validate();
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            throw new CodeEnforcerException($"Missing value for {option}.", ExitCodes.InputError);
        }

        index++;
        return args[index];
    }

    private static int ParsePositiveInt(string value, string option)
    {
        if (!int.TryParse(value, out int result) || result <= 0)
        {
            throw new CodeEnforcerException($"{option} expects a positive integer.", ExitCodes.InputError);
        }

        return result;
    }
}
