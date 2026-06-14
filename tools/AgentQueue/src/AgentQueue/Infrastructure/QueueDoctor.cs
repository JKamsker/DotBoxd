using System.Text.Json;

namespace AgentQueue.Infrastructure;

internal sealed class QueueDoctor
{
    private readonly AgentQueuePaths paths;
    private readonly FindingRepository repository;
    private readonly QueueRenderer renderer;

    public QueueDoctor(AgentQueuePaths paths, FindingRepository repository, QueueRenderer renderer)
    {
        this.paths = paths;
        this.repository = repository;
        this.renderer = renderer;
    }

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        CheckStructure(errors);
        IReadOnlyList<Finding> findings = repository.LoadAll(errors);
        CheckFindings(findings, errors);
        CheckQueues(findings, errors);
        return errors;
    }

    private void CheckStructure(List<string> errors)
    {
        foreach (string directory in new[]
        {
            paths.AgentLoopDirectory,
            paths.FindingsDirectory,
            paths.EventsDirectory,
            paths.QueuesDirectory,
            paths.ActiveDirectory
        })
        {
            if (!Directory.Exists(directory))
            {
                errors.Add("Missing directory: " + paths.ToDisplayPath(directory));
            }
        }
    }

    private void CheckFindings(IReadOnlyList<Finding> findings, List<string> errors)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> dedupKeys = new(StringComparer.OrdinalIgnoreCase);

        foreach (Finding finding in findings)
        {
            CheckFinding(finding, ids, dedupKeys, errors);
            CheckEvents(finding, errors);
        }

        CheckDuplicateReferences(findings, errors);
    }

    private void CheckFinding(
        Finding finding,
        HashSet<string> ids,
        Dictionary<string, string> dedupKeys,
        List<string> errors)
    {
        foreach (string key in new[] { "id", "area", "status", "priority", "title", "dedup_key" })
        {
            if (string.IsNullOrWhiteSpace(finding.Get(key)))
            {
                errors.Add($"Finding {Display(finding)} is missing required field '{key}'.");
            }
        }

        if (!ids.Add(finding.Id))
        {
            errors.Add("Duplicate finding ID: " + finding.Id);
        }

        if (!AgentQueueCatalog.IsStatus(finding.Status))
        {
            errors.Add($"{finding.Id} has invalid status '{finding.Status}'.");
        }

        if (!AgentQueueCatalog.IsPriority(finding.Priority))
        {
            errors.Add($"{finding.Id} has invalid priority '{finding.Priority}'.");
        }

        ValidateAreaAndFilename(finding, errors);
        ValidateDedupKey(finding, dedupKeys, errors);
        if (string.IsNullOrWhiteSpace(finding.Body))
        {
            errors.Add($"{finding.Id} has an empty body.");
        }
    }

    private void ValidateAreaAndFilename(Finding finding, List<string> errors)
    {
        try
        {
            AgentArea area = AgentQueueCatalog.RequireArea(finding.Area);
            if (!finding.Id.StartsWith(area.Prefix + "-", StringComparison.Ordinal))
            {
                errors.Add($"{finding.Id} does not match area prefix {area.Prefix}.");
            }
        }
        catch (AgentQueueException)
        {
            errors.Add($"{finding.Id} has invalid area '{finding.Area}'.");
        }

        string fileName = Path.GetFileName(finding.FilePath ?? string.Empty);
        if (!fileName.StartsWith(finding.Id + "-", StringComparison.Ordinal))
        {
            errors.Add($"{finding.Id} does not match filename {fileName}.");
        }
    }

    private static void ValidateDedupKey(
        Finding finding,
        Dictionary<string, string> dedupKeys,
        List<string> errors)
    {
        if (dedupKeys.TryGetValue(finding.DedupKey, out string? existingId))
        {
            errors.Add($"{finding.Id} duplicates dedup key from {existingId}: {finding.DedupKey}");
            return;
        }

        dedupKeys[finding.DedupKey] = finding.Id;
    }

    private void CheckEvents(Finding finding, List<string> errors)
    {
        string path = paths.EventFileFor(finding.Id);
        if (!File.Exists(path))
        {
            errors.Add($"{finding.Id} is missing event file {paths.ToDisplayPath(path)}.");
            return;
        }

        string lastStatus = string.Empty;
        foreach (string line in File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            FindingEvent? current = ReadEvent(line, finding.Id, errors);
            if (current is null)
            {
                continue;
            }

            if (lastStatus.Length > 0 && !EventTransitionMatches(lastStatus, current))
            {
                errors.Add($"{finding.Id} has implausible event transition {lastStatus} -> {current.Status}.");
            }

            lastStatus = current.Status;
        }

        if (lastStatus.Length > 0 &&
            !string.Equals(lastStatus, finding.Status, StringComparison.Ordinal))
        {
            errors.Add($"{finding.Id} status {finding.Status} does not match event log status {lastStatus}.");
        }
    }

    private static bool EventTransitionMatches(string currentStatus, FindingEvent current)
        => string.Equals(current.Status, currentStatus, StringComparison.Ordinal) ||
           AgentQueueCatalog.CanTransition(currentStatus, current.Status, force: false);

    private static void CheckDuplicateReferences(IReadOnlyList<Finding> findings, List<string> errors)
    {
        HashSet<string> ids = findings.Select(finding => finding.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (Finding finding in findings.Where(finding => finding.Status == "duplicate"))
        {
            string duplicateOf = finding.Get("duplicate_of");
            if (string.IsNullOrWhiteSpace(duplicateOf))
            {
                errors.Add($"{finding.Id} is marked duplicate without duplicate_of.");
            }
            else if (string.Equals(finding.Id, duplicateOf, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{finding.Id} must not duplicate itself.");
            }
            else if (!ids.Contains(duplicateOf))
            {
                errors.Add($"{finding.Id} duplicates unknown finding {duplicateOf}.");
            }
        }
    }

    private static FindingEvent? ReadEvent(string line, string id, List<string> errors)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("status", out JsonElement statusElement))
            {
                errors.Add($"{id} has event without status.");
                return null;
            }

            string? status = statusElement.GetString();
            if (string.IsNullOrWhiteSpace(status) || !AgentQueueCatalog.IsStatus(status))
            {
                errors.Add($"{id} has event with invalid status '{status}'.");
                return null;
            }

            return new FindingEvent(status);
        }
        catch (JsonException ex)
        {
            errors.Add($"{id} has malformed event JSON: {ex.Message}");
            return null;
        }
    }

    private void CheckQueues(IReadOnlyList<Finding> findings, List<string> errors)
    {
        foreach (AgentArea area in AgentQueueCatalog.Areas)
        {
            string queuePath = paths.QueueFileFor(area);
            string expected = renderer.Generate(area, findings);
            if (!File.Exists(queuePath))
            {
                errors.Add("Missing queue file: " + paths.ToDisplayPath(queuePath));
                continue;
            }

            string actual = File.ReadAllText(queuePath).Replace("\r\n", "\n");
            if (!actual.StartsWith(AgentQueueCatalog.QueueGeneratedWarning, StringComparison.Ordinal))
            {
                errors.Add("Missing generated warning in " + paths.ToDisplayPath(queuePath));
            }

            if (actual != expected.Replace("\r\n", "\n"))
            {
                errors.Add("Stale generated queue: " + paths.ToDisplayPath(queuePath));
            }
        }
    }

    private static string Display(Finding finding) =>
        string.IsNullOrWhiteSpace(finding.Id) ? "<unknown>" : finding.Id;

    private sealed record FindingEvent(string Status);
}
