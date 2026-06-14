using System.Reflection;
using System.Text.RegularExpressions;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for API-0013: the shipped <see cref="SandboxPolicyBuilder.GrantTimeNow"/> and
/// <see cref="SandboxPolicyBuilder.GrantRandom"/> grant helpers are the intended safe way to authorize
/// modules that request the capability-gated <c>time.now</c> and <c>random</c> runtime features served
/// by <c>SandboxHostBuilder.AddTimeBindings()</c> / <c>AddRandomBindings()</c>. The public C# API spec
/// page advertised the host-side time/random bindings but omitted the matching policy-builder helpers,
/// pushing host authors toward the generic <c>Grant(...)</c> escape hatch or source/test spelunking.
///
/// These tests first pin the real shipped builder surface (so the doc expectation is grounded in the
/// current public API, not invented), then require the public API spec to document both grant helpers,
/// the capability IDs they grant, and their deterministic <c>LogicalNow</c>/<c>RandomSeed</c>
/// relationship. A final consumer-facing smoke test proves the documented setup actually prepares time
/// and random modules without using generic <c>Grant(...)</c>.
/// </summary>
public sealed class Fix_API_0013_Tests
{
    private const string PublicApiSpecRelativePath =
        "docs/Specs/Initial/dotboxd-sandbox-spec/spec/16-public-api.md";

    [Fact]
    public void Shipped_policy_builder_exposes_time_and_random_grant_helpers()
    {
        // Grounds the documentation expectation: the public builder really does carry these helpers,
        // so omitting them from the spec is a genuine doc/code gap, not a stale finding.
        AssertPublicParameterlessBuilderHelper(nameof(SandboxPolicyBuilder.GrantTimeNow));
        AssertPublicParameterlessBuilderHelper(nameof(SandboxPolicyBuilder.GrantRandom));
    }

    [Fact]
    public void Public_api_spec_lists_time_and_random_grant_helpers()
    {
        var spec = ReadRepositoryText(PublicApiSpecRelativePath);

        // The published policy-builder surface must list both grant helpers next to the other grants
        // so consumers can discover the intended safe time/random setup from the API page alone.
        Assert.Contains("GrantTimeNow()", spec, StringComparison.Ordinal);
        Assert.Contains("GrantRandom()", spec, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_api_spec_documents_granted_capability_ids()
    {
        var spec = ReadRepositoryText(PublicApiSpecRelativePath);

        // Operators reviewing time/random grants need to know which capability IDs each helper grants
        // and how they relate to deterministic execution, mirroring the host-side binding docs.
        Assert.Matches(new Regex(@"GrantTimeNow[^\r\n]*time\.now", RegexOptions.IgnoreCase), spec);
        Assert.Matches(new Regex(@"GrantRandom[^\r\n]*random", RegexOptions.IgnoreCase), spec);
        Assert.Contains("LogicalNow", spec, StringComparison.Ordinal);
        Assert.Contains("RandomSeed", spec, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Documented_setup_prepares_time_and_random_modules_without_generic_grant()
    {
        // Mirrors the spec snippet: AddTimeBindings()/AddRandomBindings() plus the dedicated grant
        // helpers must prepare modules that request time and random, proving the documented path is a
        // complete alternative to the generic Grant(...) escape hatch.
        var host = SandboxTestHost.Create();
        var policy = SandboxPolicyBuilder.Create()
            .GrantTimeNow()
            .GrantRandom()
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 123)
            .Build();

        var timeModule = await host.ImportJsonAsync(TimeJson());
        var randomModule = await host.ImportJsonAsync(RandomJson());

        var timePlan = await host.PrepareAsync(timeModule, policy);
        var randomPlan = await host.PrepareAsync(randomModule, policy);

        Assert.NotNull(timePlan);
        Assert.NotNull(randomPlan);

        var timeResult = await host.ExecuteAsync(timePlan, "main", SandboxValue.Unit);
        var randomResult = await host.ExecuteAsync(randomPlan, "main", SandboxValue.Unit);

        Assert.True(timeResult.Succeeded, timeResult.Error?.SafeMessage);
        Assert.True(randomResult.Succeeded, randomResult.Error?.SafeMessage);
    }

    private static void AssertPublicParameterlessBuilderHelper(string name)
    {
        var method = typeof(SandboxPolicyBuilder).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(typeof(SandboxPolicyBuilder), method!.ReturnType);
    }

    private static string ReadRepositoryText(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(RepositoryRoot(), normalized);
        Assert.True(File.Exists(path), $"Missing repository file: {path}");
        return File.ReadAllText(path);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DotBoxd.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static string TimeJson()
        => """
        {
          "id": "clock",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "time.now" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I64",
              "body": [
                { "op": "return", "value": { "call": "time.nowUnixMillis", "args": [] } }
              ]
            }
          ]
        }
        """;

    private static string RandomJson()
        => """
        {
          "id": "dice",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "random" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "random.nextI32",
                    "args": [{ "i32": 0 }, { "i32": 1000 }]
                  }
                }
              ]
            }
          ]
        }
        """;
}
