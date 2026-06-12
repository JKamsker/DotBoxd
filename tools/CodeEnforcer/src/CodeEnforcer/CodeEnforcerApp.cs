namespace CodeEnforcer;

internal sealed class CodeEnforcerApp
{
    private readonly TextWriter output;
    private readonly TextWriter error;

    public CodeEnforcerApp(TextWriter output, TextWriter error)
    {
        this.output = output;
        this.error = error;
    }

    public int Run(string[] args, string currentDirectory)
    {
        try
        {
            CodeEnforcerOptions options = CodeEnforcerOptions.Parse(args);
            if (options.ShowHelp)
            {
                WriteHelp(output);
                return ExitCodes.Success;
            }

            string root = options.RootDirectory ?? RepositoryPaths.DiscoverRoot(currentDirectory);
            CodeEnforcerConfig config = CodeEnforcerConfig.Load(root, options.ConfigPath);
            options.ApplyOverrides(config);

            IReadOnlyList<CodeFile> files = CodeFileCollector.Collect(root);
            IReadOnlyList<CodeViolation> violations = new CodeEnforcerEngine().Check(files, config);
            WriteResult(violations);
            return violations.Count == 0 ? ExitCodes.Success : ExitCodes.ViolationsFound;
        }
        catch (CodeEnforcerException ex)
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

    private void WriteResult(IReadOnlyList<CodeViolation> violations)
    {
        foreach (CodeViolation violation in violations)
        {
            output.WriteLine($"{violation.Rule} {violation.Path}: {violation.Message}");
        }

        output.WriteLine(violations.Count == 0
            ? "CodeEnforcer passed."
            : $"CodeEnforcer found {violations.Count.ToStringInvariant()} violation(s).");
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("CodeEnforcer");
        writer.WriteLine("  --root <path>");
        writer.WriteLine("  --config <path>");
        writer.WriteLine("  --soft-line-limit <number>");
        writer.WriteLine("  --hard-line-limit <number>");
        writer.WriteLine("  --max-files-per-folder <number>");
    }
}
