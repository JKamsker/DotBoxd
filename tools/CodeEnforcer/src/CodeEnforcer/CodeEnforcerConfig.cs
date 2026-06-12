using System.Text.Json;

namespace CodeEnforcer;

internal sealed class CodeEnforcerConfig
{
    public int Version { get; set; } = 1;

    public int SoftLineLimit { get; set; } = 350;

    public int HardLineLimit { get; set; } = 500;

    public int MaxFilesPerFolder { get; set; } = 15;

    public List<PathExclusion> FileExclusions { get; set; } = [];

    public List<PathExclusion> FolderExclusions { get; set; } = [];

    public static CodeEnforcerConfig Load(string root, string? configPath)
    {
        string path = configPath ?? Path.Combine("tools", "CodeEnforcer", "code-enforcer.json");
        string fullPath = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
        if (!File.Exists(fullPath))
        {
            if (configPath is null)
            {
                return new CodeEnforcerConfig();
            }

            throw new CodeEnforcerException($"Config file does not exist: {fullPath}", ExitCodes.InputError);
        }

        CodeEnforcerConfig? config = JsonSerializer.Deserialize<CodeEnforcerConfig>(
            File.ReadAllText(fullPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config is null)
        {
            throw new CodeEnforcerException("Config file is empty.", ExitCodes.InputError);
        }

        config.Validate();
        return config;
    }

    public PathExclusion? FindFileExclusion(string path) =>
        FileExclusions.FirstOrDefault(exclusion => PathPattern.IsMatch(path, exclusion.Path));

    public bool IsFolderExcluded(string path) =>
        FolderExclusions.Any(exclusion => PathPattern.IsMatch(path, exclusion.Path));

    public void Validate()
    {
        if (SoftLineLimit <= 0 || HardLineLimit <= 0 || MaxFilesPerFolder <= 0)
        {
            throw new CodeEnforcerException("Limits must be positive.", ExitCodes.InputError);
        }

        if (SoftLineLimit > HardLineLimit)
        {
            throw new CodeEnforcerException("softLineLimit must be <= hardLineLimit.", ExitCodes.InputError);
        }

        foreach (PathExclusion exclusion in FileExclusions.Concat(FolderExclusions))
        {
            if (string.IsNullOrWhiteSpace(exclusion.Path))
            {
                throw new CodeEnforcerException("Exclusion paths must not be empty.", ExitCodes.InputError);
            }
        }
    }
}
