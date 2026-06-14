using System.Text.Json;

namespace AgentQueue;

internal sealed class FindingRepository
{
    private readonly AgentQueuePaths paths;

    public FindingRepository(AgentQueuePaths paths)
    {
        this.paths = paths;
    }

    public void EnsureLayout(bool forceDefaults)
    {
        Directory.CreateDirectory(paths.FindingsDirectory);
        Directory.CreateDirectory(paths.EventsDirectory);
        Directory.CreateDirectory(paths.QueuesDirectory);
        Directory.CreateDirectory(paths.ActiveDirectory);

        WriteDefault(Path.Combine(paths.AgentLoopDirectory, "README.md"), AgentQueueDefaults.Readme, forceDefaults);
        WriteDefault(Path.Combine(paths.AgentLoopDirectory, "config.json"), AgentQueueDefaults.ConfigJson, forceDefaults);
        WriteDefault(Path.Combine(paths.ActiveDirectory, "current-fix.md"), AgentQueueDefaults.CurrentFix, forceDefaults);
        WriteDefault(Path.Combine(paths.FindingsDirectory, ".gitkeep"), string.Empty, false);
        WriteDefault(Path.Combine(paths.EventsDirectory, ".gitkeep"), string.Empty, false);
    }

    public IReadOnlyList<Finding> LoadAll(List<string>? errors = null)
    {
        if (!Directory.Exists(paths.FindingsDirectory))
        {
            return [];
        }

        List<Finding> findings = [];
        foreach (string file in Directory.EnumerateFiles(paths.FindingsDirectory, "*.md").Order(StringComparer.Ordinal))
        {
            try
            {
                findings.Add(FrontmatterDocument.Read(file));
            }
            catch (AgentQueueException ex) when (errors is not null)
            {
                errors.Add(ex.Message);
            }
        }

        return findings;
    }

    public Finding FindRequired(string id)
    {
        Finding? finding = LoadAll().FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));

        return finding ?? throw new AgentQueueException($"Finding '{id}' does not exist.", ExitCodes.UserError);
    }

    public string NextId(AgentArea area)
    {
        int max = 0;
        foreach (Finding finding in LoadAll())
        {
            if (!finding.Id.StartsWith(area.Prefix + "-", StringComparison.Ordinal))
            {
                continue;
            }

            string number = finding.Id[(area.Prefix.Length + 1)..];
            if (int.TryParse(number, out int parsed))
            {
                max = Math.Max(max, parsed);
            }
        }

        return area.Prefix + "-" + (max + 1).ToString("D4");
    }

    public string SaveNew(Finding finding)
    {
        AgentArea area = AgentQueueCatalog.RequireArea(finding.Area);
        string slug = SlugGenerator.Create(finding.Title);
        string fileName = finding.Id + "-" + slug + ".md";
        string filePath = Path.Combine(paths.FindingsDirectory, fileName);
        if (File.Exists(filePath))
        {
            throw new AgentQueueException($"Finding file already exists: {paths.ToDisplayPath(filePath)}.", ExitCodes.UserError);
        }

        finding.FilePath = filePath;
        Save(finding);
        return paths.ToDisplayPath(filePath);
    }

    public void Save(Finding finding)
    {
        if (finding.FilePath is null)
        {
            throw new AgentQueueException("Finding does not have a file path.", ExitCodes.InternalError);
        }

        AtomicFile.WriteAllText(finding.FilePath, FrontmatterDocument.Write(finding));
    }

    public void AppendEvent(string id, IReadOnlyDictionary<string, string> fields)
    {
        Directory.CreateDirectory(paths.EventsDirectory);
        string eventPath = paths.EventFileFor(id);
        string json = JsonSerializer.Serialize(fields.Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        File.AppendAllText(eventPath, json + Environment.NewLine);
    }

    public void EnsureDedupKeyIsUnique(string dedupKey)
    {
        Finding? existing = LoadAll().FirstOrDefault(finding =>
            string.Equals(finding.DedupKey, dedupKey, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return;
        }

        string file = existing.FilePath is null ? "<unknown>" : paths.ToDisplayPath(existing.FilePath);
        throw new AgentQueueException(
            $"Duplicate dedup key already exists on {existing.Id}: {file}.",
            ExitCodes.DuplicateFinding);
    }

    private static void WriteDefault(string path, string content, bool force)
    {
        if (!force && File.Exists(path))
        {
            return;
        }

        AtomicFile.WriteAllText(path, content.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? content
            : content + Environment.NewLine);
    }
}
