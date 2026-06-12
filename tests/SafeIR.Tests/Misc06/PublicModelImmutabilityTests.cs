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
    public void Sandbox_type_copies_argument_inputs()
    {
        var arguments = new List<SandboxType> { SandboxType.I32 };
        var type = new SandboxType("List", arguments);
        var hash = type.GetHashCode();
        arguments[0] = SandboxType.String;

        var updateArguments = new List<SandboxType> { SandboxType.String };
        var updated = type with { Arguments = updateArguments };
        updateArguments[0] = SandboxType.I64;

        Assert.Equal("List<I32>", type.ToString());
        Assert.Equal(hash, type.GetHashCode());
        Assert.Equal("List<String>", updated.ToString());
        Assert.False(type.Arguments is SandboxType[]);
        Assert.False(updated.Arguments is SandboxType[]);
    }

    [Fact]
    public void Sandbox_policy_copies_grant_inputs()
    {
        var grants = new List<CapabilityGrant> { new("log.write", new Dictionary<string, string>()) };
        var policy = new SandboxPolicy("policy", SandboxEffect.Audit, grants, new ResourceLimits());
        var hash = policy.Hash;
        grants[0] = new CapabilityGrant("random", new Dictionary<string, string>());

        var updateGrants = new List<CapabilityGrant> { new("time.now", new Dictionary<string, string>()) };
        var updated = policy with { Grants = updateGrants };
        var updatedHash = updated.Hash;
        updateGrants[0] = new CapabilityGrant("random", new Dictionary<string, string>());

        Assert.True(policy.GrantsCapability("log.write"));
        Assert.False(policy.GrantsCapability("random"));
        Assert.Equal(hash, policy.Hash);
        Assert.True(updated.GrantsCapability("time.now"));
        Assert.False(updated.GrantsCapability("random"));
        Assert.Equal(updatedHash, updated.Hash);
        Assert.False(policy.Grants is CapabilityGrant[]);
        Assert.False(updated.Grants is CapabilityGrant[]);
    }

    [Fact]
    public void Binding_registry_copies_descriptor_parameter_inputs()
    {
        var parameters = new List<SandboxType> { SandboxType.I32 };
        var descriptor = new BindingDescriptor(
            "test.binding",
            SemVersion.One,
            parameters,
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromInt32(0)),
            CompiledBinding.RuntimeStub(
                typeof(Runtime.CompiledRuntime).FullName!,
                nameof(Runtime.CompiledRuntime.CallBinding)));
        var registry = new BindingRegistry([descriptor]);
        var manifestHash = registry.ManifestHash;
        parameters[0] = SandboxType.String;

        var exposed = registry.GetDescriptor("test.binding");
        Assert.True(registry.TryGet("test.binding", out var signature));

        Assert.Equal(manifestHash, registry.ManifestHash);
        Assert.Equal(SandboxType.I32, exposed.Parameters[0]);
        Assert.Equal(SandboxType.I32, signature.Parameters[0]);
        Assert.False(exposed.Parameters is SandboxType[]);
        Assert.False(signature.Parameters is SandboxType[]);
    }

    [Fact]
    public void Execution_plan_copies_binding_reference_sets()
    {
        var references = new HashSet<string>(StringComparer.Ordinal) { "math.abs" };
        var plan = new ExecutionPlan(
            "module",
            "plan",
            new ExecutionPlanSeal("seal"),
            "policy",
            "bindings",
            EmptyModule(),
            SandboxPolicyBuilder.Create().Build(),
            new BindingRegistryBuilder().Build(),
            new ResourceLimits(),
            new Dictionary<string, FunctionAnalysis>
            {
                ["main"] = new(SandboxType.I32, SandboxEffect.Cpu, true)
            },
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["main"] = references
            });
        references.Add("file.writeText");

        Assert.Contains("math.abs", plan.BindingReferences["main"]);
        Assert.DoesNotContain("file.writeText", plan.BindingReferences["main"]);
        Assert.False(plan.BindingReferences["main"] is HashSet<string>);
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

    [Fact]
    public void Sandbox_values_copy_collection_inputs()
    {
        var listBacking = new List<SandboxValue> { SandboxValue.FromInt32(1) };
        var list = (ListValue)SandboxValue.FromList(listBacking);
        listBacking.Add(SandboxValue.FromInt32(2));

        var mapBacking = new Dictionary<SandboxValue, SandboxValue>
        {
            [SandboxValue.FromString("first")] = SandboxValue.FromInt32(1)
        };
        var map = (MapValue)SandboxValue.FromMap(mapBacking, SandboxType.String, SandboxType.I32);
        mapBacking[SandboxValue.FromString("second")] = SandboxValue.FromInt32(2);

        Assert.Single(list.Values);
        Assert.Single(map.Values);
    }

    [Fact]
    public void Validation_exception_copies_diagnostic_inputs()
    {
        var diagnostics = new List<SandboxDiagnostic>
        {
            new("E-ONE", "first")
        };
        var exception = new SandboxValidationException(diagnostics);
        diagnostics.Add(new SandboxDiagnostic("E-TWO", "second"));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("E-ONE", diagnostic.Code);
    }

    [Fact]
    public void Audit_event_and_sink_copy_field_inputs()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["resourceKind"] = "network"
        };
        var auditEvent = new SandboxAuditEvent(
            SandboxRunId.New(),
            "BindingCall",
            DateTimeOffset.UtcNow,
            true,
            Fields: fields);
        fields["resourceKind"] = "mutated-before-write";

        var sink = new InMemoryAuditSink();
        sink.Write(auditEvent);
        fields["resourceKind"] = "mutated-after-write";

        Assert.Equal("network", auditEvent.Fields!["resourceKind"]);
        Assert.Equal("network", sink.Events[0].Fields!["resourceKind"]);
    }

    [Fact]
    public void Execution_result_copies_audit_event_list_inputs()
    {
        var events = new List<SandboxAuditEvent>
        {
            new(SandboxRunId.New(), "RunSummary", DateTimeOffset.UtcNow, true)
        };
        var result = new SandboxExecutionResult
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = new ResourceMeter(new ResourceLimits(MaxFuel: 1_000)).Snapshot(),
            AuditEvents = events,
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = "module",
            PlanHash = "plan",
            PolicyHash = "policy"
        };
        events.Clear();

        Assert.Single(result.AuditEvents);
    }

    private static SandboxModule EmptyModule()
        => new("module", SemVersion.One, SemVersion.One, [], [EmptyFunction()], new Dictionary<string, string>());

    private static SandboxFunction EmptyFunction()
        => new("main", true, [], SandboxType.I32, [new ReturnStatement(Literal, Span)]);
}
