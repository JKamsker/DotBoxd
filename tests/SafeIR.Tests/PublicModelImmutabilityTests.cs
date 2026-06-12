using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PublicModelImmutabilityTests
{
    private static readonly SourceSpan Span = new(1, 1);
    private static readonly Expression Literal = new LiteralExpression(SandboxValue.FromInt32(1), Span);

    [Fact]
    public void Sandbox_module_and_function_copy_collection_inputs()
    {
        var parameters = new List<Parameter> { new("value", SandboxType.I32) };
        var body = new List<Statement> { new ReturnStatement(Literal, Span) };
        var function = new SandboxFunction("main", true, parameters, SandboxType.I32, body);
        parameters.Add(new Parameter("other", SandboxType.I32));
        body.Clear();

        var capabilities = new List<CapabilityRequest> { new("log.write", null) };
        var functions = new List<SandboxFunction> { function };
        var metadata = new Dictionary<string, string> { ["pluginId"] = "safe" };
        var module = new SandboxModule("module", SemVersion.One, SemVersion.One, capabilities, functions, metadata);
        capabilities.Clear();
        functions.Clear();
        metadata["pluginId"] = "mutated";

        Assert.Single(function.Parameters);
        Assert.Single(function.Body);
        Assert.Single(module.CapabilityRequests);
        Assert.Single(module.Functions);
        Assert.Equal("safe", module.Metadata["pluginId"]);
    }

    [Fact]
    public void Statement_and_expression_nodes_copy_collection_inputs()
    {
        var statements = new List<Statement> { new ReturnStatement(Literal, Span) };
        var ifStatement = new IfStatement(Literal, statements, statements, Span);
        var whileStatement = new WhileStatement(Literal, statements, Span);
        var rangeStatement = new ForRangeStatement("i", Literal, Literal, statements, Span);
        var arguments = new List<Expression> { Literal };
        var call = new CallExpression("test", arguments, null, Span);
        statements.Clear();
        arguments.Clear();

        Assert.Single(ifStatement.Then);
        Assert.Single(ifStatement.Else);
        Assert.Single(whileStatement.Body);
        Assert.Single(rangeStatement.Body);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void With_updates_copy_collection_inputs()
    {
        var module = EmptyModule();
        var functions = new List<SandboxFunction> { EmptyFunction() };
        var updated = module with { Functions = functions };
        functions.Clear();

        Assert.Single(updated.Functions);
    }

    [Fact]
    public void Plugin_manifest_copies_collection_inputs()
    {
        var effects = new List<string> { "Cpu" };
        var settings = new List<LiveSettingDefinition> { new("Enabled", "bool", true) };
        var subscriptions = new List<HookSubscriptionManifest> { new("DamageEvent", "Kernel") };
        var manifest = new PluginManifest("plugin", "contract", ExecutionMode.Interpreted, effects, settings, subscriptions);
        effects.Clear();
        settings.Clear();
        subscriptions.Clear();

        Assert.Single(manifest.Effects);
        Assert.Single(manifest.LiveSettings);
        Assert.Single(manifest.Subscriptions);
    }

    private static SandboxModule EmptyModule()
        => new("module", SemVersion.One, SemVersion.One, [], [EmptyFunction()], new Dictionary<string, string>());

    private static SandboxFunction EmptyFunction()
        => new("main", true, [], SandboxType.I32, [new ReturnStatement(Literal, Span)]);
}
