namespace AgentQueue.Tests;

public sealed class AgentQueueWorkflowTests
{
    [Fact]
    public void InitCreatesDirectoriesAndEmptyQueues()
    {
        using AgentQueueHarness harness = new();

        Assert.Equal(0, harness.Run("init"));
        Assert.Equal(0, harness.Run("init"));
        Assert.True(Directory.Exists(Path.Combine(harness.Root, "docs", "agent-loop", "findings")));
        Assert.True(File.Exists(Path.Combine(harness.Root, "docs", "agent-loop", "queues", "correctness.md")));
        Assert.Equal(0, harness.Run("render", "--check"));
        Assert.Equal(0, harness.Run("doctor"));
    }

    [Fact]
    public void AppendClaimFixVerifyCompletesFinding()
    {
        using AgentQueueHarness harness = new();
        string bodyFile = harness.WriteBody("""
            ## Claim

            Example correctness issue.

            ## Evidence

            Example evidence.

            ## Suggested test

            Example test.
            """);

        Assert.Equal(0, harness.Run("append",
            "--area", "correctness",
            "--priority", "high",
            "--title", "Example bug",
            "--dedup-key", "correctness/example/bug",
            "--agent", "correctness-auditor",
            "--body-file", bodyFile));
        Assert.Contains("CREATED COR-0001", harness.LastOutput);

        Assert.Equal(0, harness.Run("claim",
            "--area", "correctness",
            "--agent", "fixer",
            "--branch", "fix/COR-0001"));
        Assert.Equal(0, harness.Run("fix", "COR-0001",
            "--agent", "fixer",
            "--commit", "abc123",
            "--notes", "Fixed example"));
        Assert.Equal(0, harness.Run("verify", "COR-0001",
            "--agent", "verifier",
            "--commit", "def456",
            "--cmd", "dotnet test",
            "--notes", "Verified example"));

        string queue = File.ReadAllText(Path.Combine(harness.Root, "docs", "agent-loop", "queues", "correctness.md"));
        Assert.Contains("- [x] `COR-0001` high - Example bug", queue);
        Assert.Equal(0, harness.Run("render", "--check"));
        Assert.Equal(0, harness.Run("doctor"));
    }

    [Fact]
    public void AppendRejectsDuplicateDedupKey()
    {
        using AgentQueueHarness harness = new();
        string bodyFile = harness.WriteBody("## Claim" + Environment.NewLine + "Example.");

        Assert.Equal(0, harness.Run("append",
            "--area", "correctness",
            "--priority", "high",
            "--title", "First bug",
            "--dedup-key", "correctness/example/bug",
            "--agent", "auditor",
            "--body-file", bodyFile));
        Assert.Equal(3, harness.Run("append",
            "--area", "correctness",
            "--priority", "high",
            "--title", "Second bug",
            "--dedup-key", "correctness/example/bug",
            "--agent", "auditor",
            "--body-file", bodyFile));
    }

    [Fact]
    public void VerifyRejectsSelfVerification()
    {
        using AgentQueueHarness harness = new();
        string bodyFile = harness.WriteBody("## Claim" + Environment.NewLine + "Example.");
        Assert.Equal(0, harness.Run("append",
            "--area", "correctness",
            "--priority", "high",
            "--title", "Example bug",
            "--dedup-key", "correctness/example/bug",
            "--agent", "auditor",
            "--body-file", bodyFile));
        Assert.Equal(0, harness.Run("claim", "COR-0001", "--agent", "fixer"));
        Assert.Equal(0, harness.Run("fix", "COR-0001", "--agent", "fixer", "--notes", "Fixed"));

        Assert.Equal(4, harness.Run("verify", "COR-0001", "--agent", "fixer", "--notes", "Verified"));
    }

    [Fact]
    public void RenderCheckDetectsManualQueueDrift()
    {
        using AgentQueueHarness harness = new();

        Assert.Equal(0, harness.Run("init"));
        File.AppendAllText(Path.Combine(harness.Root, "docs", "agent-loop", "queues", "correctness.md"), "manual edit");

        Assert.Equal(2, harness.Run("render", "--check"));
        Assert.Contains("STALE", harness.LastOutput);
    }
}
