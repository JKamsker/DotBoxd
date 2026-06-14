namespace AgentQueue.Infrastructure;

internal sealed class QueueMutationCommands
{
    private readonly FindingRepository repository;
    private readonly QueueRenderer renderer;
    private readonly ISystemClock clock;
    private readonly TextWriter output;

    public QueueMutationCommands(
        FindingRepository repository,
        QueueRenderer renderer,
        ISystemClock clock,
        TextWriter output)
    {
        this.repository = repository;
        this.renderer = renderer;
        this.clock = clock;
        this.output = output;
    }

    public int Append(CommandLine commandLine)
    {
        repository.EnsureLayout(forceDefaults: false);
        AgentArea area = AgentQueueCatalog.RequireArea(commandLine.RequireOption("area"));
        string priority = RequirePriority(commandLine.RequireOption("priority"));
        string title = commandLine.RequireOption("title");
        string dedupKey = commandLine.RequireOption("dedup-key");
        string agent = commandLine.RequireOption("agent");
        string bodyFile = commandLine.RequireOption("body-file");
        string commit = commandLine.GetOption("commit") ?? string.Empty;

        if (!File.Exists(bodyFile))
        {
            throw new AgentQueueException($"Body file does not exist: {bodyFile}.", ExitCodes.UserError);
        }

        repository.EnsureDedupKeyIsUnique(dedupKey);
        string id = repository.NextId(area);
        string body = BuildFindingBody(id, title, File.ReadAllText(bodyFile));
        Finding finding = Finding.Create(id, area, priority, title, dedupKey, agent, commit, clock.UtcNow, body);
        string file = repository.SaveNew(finding);
        AppendEvent(id, agent, "created", "open", commit, "Created finding");
        renderer.RenderAll(repository.LoadAll());
        WriteCreated(commandLine, id, file, area);
        return ExitCodes.Success;
    }

    public int Claim(CommandLine commandLine)
    {
        string agent = commandLine.RequireOption("agent");
        string branch = commandLine.GetOption("branch") ?? string.Empty;
        Finding finding = commandLine.Positionals.Count > 0
            ? repository.FindRequired(commandLine.Positionals[0])
            : FindNextOpen(commandLine.RequireOption("area"));

        if (finding.Status == "claimed")
        {
            EnsureClaimIsIdempotent(finding, agent, branch);
            WriteFindingResult(commandLine, "CLAIMED", finding);
            return ExitCodes.Success;
        }

        EnsureTransition(finding.Status, "claimed");
        DateTimeOffset now = clock.UtcNow;
        finding.Status = "claimed";
        finding.Set("claimed_by", agent);
        finding.Set("claimed_at", now.ToString("O"));
        finding.Set("claim_branch", branch);
        finding.Set("updated_at", now.ToString("O"));
        repository.Save(finding);
        AppendEvent(finding.Id, agent, "claimed", "claimed", commandLine.GetOption("commit") ?? string.Empty, "Claimed finding");
        RenderFindingArea(finding);
        WriteFindingResult(commandLine, "CLAIMED", finding);
        return ExitCodes.Success;
    }

    public int Release(CommandLine commandLine)
    {
        Finding finding = repository.FindRequired(commandLine.RequireArgument("release"));
        string agent = commandLine.RequireOption("agent");
        string reason = commandLine.RequireOption("reason");
        EnsureTransition(finding.Status, "open");
        DateTimeOffset now = clock.UtcNow;
        finding.Status = "open";
        finding.Set("claimed_by", string.Empty);
        finding.Set("claimed_at", string.Empty);
        finding.Set("claim_branch", string.Empty);
        finding.Set("updated_at", now.ToString("O"));
        repository.Save(finding);
        AppendEvent(finding.Id, agent, "released", "open", string.Empty, reason);
        RenderFindingArea(finding);
        WriteFindingResult(commandLine, "RELEASED", finding);
        return ExitCodes.Success;
    }

    public int Fix(CommandLine commandLine)
    {
        Finding finding = repository.FindRequired(commandLine.RequireArgument("fix"));
        string agent = commandLine.RequireOption("agent");
        string notes = commandLine.RequireOption("notes");
        EnsureTransition(finding.Status, "fixed_pending_verification");
        DateTimeOffset now = clock.UtcNow;
        finding.Status = "fixed_pending_verification";
        finding.Set("fixed_by", agent);
        finding.Set("fixed_at", now.ToString("O"));
        finding.Set("fixed_commit", commandLine.GetOption("commit") ?? string.Empty);
        finding.Set("updated_at", now.ToString("O"));
        repository.Save(finding);
        AppendEvent(finding.Id, agent, "fixed", finding.Status, finding.Get("fixed_commit"), notes);
        RenderFindingArea(finding);
        WriteFindingResult(commandLine, "FIXED", finding);
        return ExitCodes.Success;
    }

    public int Verify(CommandLine commandLine)
    {
        Finding finding = repository.FindRequired(commandLine.RequireArgument("verify"));
        string agent = commandLine.RequireOption("agent");
        string notes = commandLine.RequireOption("notes");
        if (!commandLine.HasOption("allow-self-verify") &&
            string.Equals(finding.Get("fixed_by"), agent, StringComparison.OrdinalIgnoreCase))
        {
            throw new AgentQueueException("A fixer must not verify its own fix.", ExitCodes.InvalidTransition);
        }

        EnsureTransition(finding.Status, "verified");
        DateTimeOffset now = clock.UtcNow;
        finding.Status = "verified";
        finding.Set("verified_by", agent);
        finding.Set("verified_at", now.ToString("O"));
        finding.Set("verified_commit", commandLine.GetOption("commit") ?? string.Empty);
        finding.Set("updated_at", now.ToString("O"));
        repository.Save(finding);
        AppendEvent(finding.Id, agent, "verified", finding.Status, finding.Get("verified_commit"), notes, commandLine.GetOption("cmd"));
        RenderFindingArea(finding);
        WriteFindingResult(commandLine, "VERIFIED", finding);
        return ExitCodes.Success;
    }

    public int Reopen(CommandLine commandLine)
    {
        Finding finding = repository.FindRequired(commandLine.RequireArgument("reopen"));
        string agent = commandLine.RequireOption("agent");
        string reason = commandLine.RequireOption("reason");
        EnsureTransition(finding.Status, "open", commandLine.HasOption("force"));
        finding.Status = "open";
        finding.Set("claimed_by", string.Empty);
        finding.Set("claimed_at", string.Empty);
        finding.Set("claim_branch", string.Empty);
        finding.Set("updated_at", clock.UtcNow.ToString("O"));
        repository.Save(finding);
        AppendEvent(finding.Id, agent, "reopened", "open", string.Empty, reason);
        RenderFindingArea(finding);
        WriteFindingResult(commandLine, "REOPENED", finding);
        return ExitCodes.Success;
    }

    public int Finalize(CommandLine commandLine, string status)
    {
        Finding finding = repository.FindRequired(commandLine.RequireArgument(status));
        string agent = commandLine.RequireOption("agent");
        string reason = commandLine.RequireOption("reason");
        if (status == "duplicate")
        {
            string duplicateOf = commandLine.RequireOption("of");
            Finding canonical = repository.FindRequired(duplicateOf);
            if (string.Equals(canonical.Id, finding.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new AgentQueueException(
                    $"Finding {finding.Id} must not duplicate itself.",
                    ExitCodes.InvalidTransition);
            }

            finding.Set("duplicate_of", canonical.Id);
        }

        EnsureTransition(finding.Status, status);
        finding.Status = status;
        finding.Set("updated_at", clock.UtcNow.ToString("O"));
        repository.Save(finding);
        AppendEvent(finding.Id, agent, status, status, string.Empty, reason);
        RenderFindingArea(finding);
        WriteFindingResult(commandLine, status.ToUpperInvariant(), finding);
        return ExitCodes.Success;
    }

    public int Note(CommandLine commandLine)
    {
        Finding finding = repository.FindRequired(commandLine.RequireArgument("note"));
        string agent = commandLine.RequireOption("agent");
        string message = commandLine.RequireOption("message");
        AppendEvent(finding.Id, agent, "note", finding.Status, string.Empty, message);
        if (commandLine.HasOption("write-body"))
        {
            finding.Body = finding.Body.TrimEnd() + Environment.NewLine + Environment.NewLine +
                "## Notes" + Environment.NewLine + Environment.NewLine +
                "- " + clock.UtcNow.ToString("O") + " " + message + Environment.NewLine;
            repository.Save(finding);
        }

        WriteFindingResult(commandLine, "NOTED", finding);
        return ExitCodes.Success;
    }

    private Finding FindNextOpen(string areaName)
    {
        AgentArea area = AgentQueueCatalog.RequireArea(areaName);
        Finding? finding = FindingSort.Order(repository.LoadAll())
            .FirstOrDefault(candidate => candidate.Area == area.Name && candidate.Status == "open");
        return finding ?? throw new AgentQueueException($"No open findings in area '{area.Name}'.", ExitCodes.UserError);
    }

    private void RenderFindingArea(Finding finding) =>
        renderer.RenderAll(repository.LoadAll());

    private void AppendEvent(
        string id,
        string agent,
        string type,
        string status,
        string commit,
        string message,
        string? command = null)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal)
        {
            ["at"] = clock.UtcNow.ToString("O"),
            ["agent"] = agent,
            ["type"] = type,
            ["status"] = status,
            ["commit"] = commit,
            ["message"] = message,
            ["command"] = command ?? string.Empty
        };
        repository.AppendEvent(id, fields);
    }

    private static string RequirePriority(string priority) =>
        AgentQueueCatalog.IsPriority(priority)
            ? priority
            : throw new AgentQueueException($"Unknown priority '{priority}'.", ExitCodes.UserError);

    private static void EnsureTransition(string current, string next, bool force = false)
    {
        if (!AgentQueueCatalog.CanTransition(current, next, force))
        {
            throw new AgentQueueException($"Invalid status transition {current} -> {next}.", ExitCodes.InvalidTransition);
        }
    }

    private static void EnsureClaimIsIdempotent(Finding finding, string agent, string branch)
    {
        if (string.Equals(finding.Get("claimed_by"), agent, StringComparison.Ordinal) &&
            string.Equals(finding.Get("claim_branch"), branch, StringComparison.Ordinal))
        {
            return;
        }

        throw new AgentQueueException(
            $"Finding {finding.Id} is already claimed by {finding.Get("claimed_by")} on branch {finding.Get("claim_branch")}.",
            ExitCodes.InvalidTransition);
    }

    private static string BuildFindingBody(string id, string title, string body) =>
        "# " + id + ": " + title.Trim() + Environment.NewLine + Environment.NewLine + body.Trim() + Environment.NewLine;

    private void WriteCreated(CommandLine commandLine, string id, string file, AgentArea area)
    {
        if (commandLine.HasOption("json"))
        {
            JsonOutput.Write(output, new { id, file, queue = "docs/agent-loop/queues/" + area.QueueFile });
            return;
        }

        output.WriteLine("CREATED " + id);
        output.WriteLine("file=" + file);
        output.WriteLine("queue=docs/agent-loop/queues/" + area.QueueFile);
    }

    private void WriteFindingResult(CommandLine commandLine, string verb, Finding finding)
    {
        string file = finding.FilePath ?? string.Empty;
        if (commandLine.HasOption("json"))
        {
            JsonOutput.Write(output, new { id = finding.Id, status = finding.Status, file });
            return;
        }

        output.WriteLine(verb + " " + finding.Id);
        output.WriteLine("file=" + file);
    }
}
