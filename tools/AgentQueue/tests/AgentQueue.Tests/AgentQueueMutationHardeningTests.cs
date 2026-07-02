namespace AgentQueue.Tests;

public sealed class AgentQueueMutationHardeningTests
{
    [Fact]
    public void FixRejectsRepeatedSameStatusMutation()
    {
        using AgentQueueHarness harness = new();
        AppendExampleFinding(harness, priority: "high");
        Assert.Equal(0, harness.Run("claim", "COR-0001", "--agent", "fixer"));
        Assert.Equal(0, harness.Run(
            "fix",
            "COR-0001",
            "--agent", "fixer",
            "--commit", "abc123",
            "--notes", "Fixed"));

        Assert.Equal(4, harness.Run(
            "fix",
            "COR-0001",
            "--agent", "fixer",
            "--commit", "def456",
            "--notes", "Changed evidence"));

        string finding = File.ReadAllText(FindingFile(harness));
        string eventFile = Path.Combine(harness.Root, "docs", "agent-loop", "events", "COR-0001.jsonl");
        Assert.Contains("fixed_commit: abc123", finding);
        Assert.DoesNotContain("fixed_commit: def456", finding);
        Assert.Single(File.ReadLines(eventFile), line => line.Contains("\"type\":\"fixed\"", StringComparison.Ordinal));
    }

    [Fact]
    public void DoctorRejectsFindingStatusThatDoesNotMatchEventLog()
    {
        using AgentQueueHarness harness = new();
        AppendExampleFinding(harness, priority: "medium");
        string content = File.ReadAllText(FindingFile(harness))
            .Replace("status: open", "status: verified", StringComparison.Ordinal);
        File.WriteAllText(FindingFile(harness), content);
        Assert.Equal(0, harness.Run("render"));

        Assert.Equal(2, harness.Run("doctor"));
        Assert.Contains(
            "COR-0001 status verified does not match event log status open.",
            harness.LastOutput);
    }

    [Fact]
    public void MutationRejectsFindingIdThatEscapesEventDirectory()
    {
        using AgentQueueHarness harness = new();
        Assert.Equal(0, harness.Run("init"));
        WriteEscapingIdFinding(harness);

        Assert.Equal(2, harness.Run("claim", "../outside", "--agent", "fixer"));

        Assert.Contains("Invalid finding id '../outside'.", harness.LastError);
        Assert.False(File.Exists(Path.Combine(harness.Root, "docs", "agent-loop", "outside.jsonl")));
    }

    [Fact]
    public void DoctorReportsEscapingFindingIdOnce()
    {
        using AgentQueueHarness harness = new();
        Assert.Equal(0, harness.Run("init"));
        WriteEscapingIdFinding(harness);

        Assert.Equal(2, harness.Run("doctor"));

        Assert.Equal(1, CountOccurrences(harness.LastOutput, "Invalid finding id '../outside'."));
    }

    private static void AppendExampleFinding(AgentQueueHarness harness, string priority)
    {
        string bodyFile = harness.WriteBody("## Claim" + Environment.NewLine + "Example.");
        Assert.Equal(0, harness.Run("append",
            "--area", "correctness",
            "--priority", priority,
            "--title", "Example bug",
            "--dedup-key", "correctness/example/bug",
            "--agent", "auditor",
            "--body-file", bodyFile));
    }

    private static string FindingFile(AgentQueueHarness harness)
        => Directory.GetFiles(
            Path.Combine(harness.Root, "docs", "agent-loop", "findings"),
            "COR-0001-*.md").Single();

    private static void WriteEscapingIdFinding(AgentQueueHarness harness)
    {
        string findingPath = Path.Combine(
            harness.Root,
            "docs",
            "agent-loop",
            "findings",
            "COR-0001-malicious.md");
        File.WriteAllText(findingPath, """
            ---
            id: ../outside
            area: correctness
            status: open
            priority: high
            title: Malicious id
            dedup_key: correctness/malicious/id
            created_at: 2026-06-12T10:00:00.0000000+00:00
            created_by: attacker
            created_commit:
            updated_at: 2026-06-12T10:00:00.0000000+00:00
            claimed_by:
            claimed_at:
            claim_branch:
            fixed_by:
            fixed_at:
            fixed_commit:
            verified_by:
            verified_at:
            verified_commit:
            duplicate_of:
            ---

            # malicious
            """);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
