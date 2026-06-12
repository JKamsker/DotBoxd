namespace SafeIR.Tests;

public sealed class SafeIrJsonExporterTests
{
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public void Export_round_trips_control_flow_and_generic_type_shapes()
    {
        var module = new SandboxModule(
            "json-exporter",
            SemVersion.One,
            SemVersion.One,
            [
                new CapabilityRequest("game.message.write", null),
                new CapabilityRequest("test.audit", "observe test execution")
            ],
            [
                new SandboxFunction(
                    "Helper",
                    false,
                    [new Parameter("input", SandboxType.String)],
                    SandboxType.String,
                    [new ReturnStatement(new VariableExpression("input", Span), Span)]),
                MainFunction(),
                GenericFunction()
            ],
            new Dictionary<string, string>
            {
                ["pluginId"] = "json-exporter",
                ["kernel"] = "JsonExporterKernel"
            });

        var json = SafeIrJsonExporter.Export(module, indented: true);
        var roundTrip = SafeIrJsonImporter.Import(json);

        Assert.Equal("json-exporter", roundTrip.Id);
        Assert.Equal(SemVersion.One, roundTrip.TargetSandboxVersion);
        Assert.Equal(2, roundTrip.CapabilityRequests.Count);
        Assert.Null(roundTrip.CapabilityRequests[0].Reason);

        var main = Assert.Single(roundTrip.Functions, function => function.Id == "Main");
        Assert.IsType<AssignmentStatement>(main.Body[0]);
        Assert.IsType<IfStatement>(main.Body[1]);
        Assert.IsType<ForRangeStatement>(main.Body[2]);
        Assert.IsType<WhileStatement>(main.Body[3]);

        var ret = Assert.IsType<ReturnStatement>(main.Body[4]);
        var length = Assert.IsType<CallExpression>(ret.Value);
        var empty = Assert.IsType<CallExpression>(Assert.Single(length.Arguments));
        Assert.Equal(SandboxType.I32, empty.GenericType);

        var generic = Assert.Single(roundTrip.Functions, function => function.Id == "Generic");
        Assert.Equal(SandboxType.Map(SandboxType.String, SandboxType.I32), generic.ReturnType);
    }

    [Fact]
    public void Export_round_trips_all_supported_scalar_literal_shapes()
    {
        var module = new SandboxModule(
            "literal-exporter",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "Literals",
                    true,
                    [],
                    SandboxType.Unit,
                    [
                        Literal(SandboxValue.FromBool(true)),
                        Literal(SandboxValue.FromInt32(7)),
                        Literal(SandboxValue.FromInt64(8L)),
                        Literal(SandboxValue.FromDouble(1.5D)),
                        Literal(SandboxValue.FromString("text")),
                        Literal(SandboxValue.FromPlayerId("player-1")),
                        Literal(SandboxValue.FromItemId("item-1")),
                        Literal(SandboxValue.FromQuestId("quest-1")),
                        Literal(SandboxValue.FromMapId("map-1")),
                        Literal(SandboxValue.FromPath("config/plugin.json")),
                        Literal(SandboxValue.FromUri("https://example.test/config")),
                        new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)
                    ])
            ],
            new Dictionary<string, string>());

        var roundTrip = SafeIrJsonImporter.Import(SafeIrJsonExporter.Export(module));
        var function = Assert.Single(roundTrip.Functions);

        Assert.Collection(
            function.Body,
            statement => Assert.IsType<BoolValue>(LiteralValue(statement)),
            statement => Assert.IsType<I32Value>(LiteralValue(statement)),
            statement => Assert.IsType<I64Value>(LiteralValue(statement)),
            statement => Assert.IsType<F64Value>(LiteralValue(statement)),
            statement => Assert.IsType<StringValue>(LiteralValue(statement)),
            statement => Assert.IsType<OpaqueIdValue>(LiteralValue(statement)),
            statement => Assert.IsType<OpaqueIdValue>(LiteralValue(statement)),
            statement => Assert.IsType<OpaqueIdValue>(LiteralValue(statement)),
            statement => Assert.IsType<OpaqueIdValue>(LiteralValue(statement)),
            statement => Assert.IsType<SandboxPathValue>(LiteralValue(statement)),
            statement => Assert.IsType<SandboxUriValue>(LiteralValue(statement)),
            statement => Assert.IsType<UnitValue>(LiteralValue(statement)));
    }

    private static SandboxFunction MainFunction()
        => new(
            "Main",
            true,
            [
                new Parameter("flag", SandboxType.Bool),
                new Parameter("count", SandboxType.I32)
            ],
            SandboxType.I32,
            [
                new AssignmentStatement("total", I32(0), Span),
                new IfStatement(
                    new UnaryExpression("!", new VariableExpression("flag", Span), Span),
                    [
                        new AssignmentStatement(
                            "total",
                            new BinaryExpression(new VariableExpression("count", Span), "+", I32(1), Span),
                            Span)
                    ],
                    [
                        new ExpressionStatement(
                            new CallExpression("trace", [new LiteralExpression(SandboxValue.FromString("else"), Span)], null, Span),
                            Span)
                    ],
                    Span),
                new ForRangeStatement(
                    "i",
                    I32(0),
                    I32(2),
                    [
                        new AssignmentStatement(
                            "total",
                            new BinaryExpression(
                                new VariableExpression("total", Span),
                                "+",
                                new VariableExpression("i", Span),
                                Span),
                            Span)
                    ],
                    Span),
                new WhileStatement(
                    new LiteralExpression(SandboxValue.FromBool(false), Span),
                    [new ExpressionStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)],
                    Span),
                new ReturnStatement(
                    new CallExpression(
                        "list.length",
                        [new CallExpression("list.empty", [], SandboxType.I32, Span)],
                        null,
                        Span),
                    Span)
            ]);

    private static SandboxFunction GenericFunction()
        => new(
            "Generic",
            true,
            [],
            SandboxType.Map(SandboxType.String, SandboxType.I32),
            [
                new ReturnStatement(
                    new CallExpression(
                        "map.empty",
                        [],
                        SandboxType.Map(SandboxType.String, SandboxType.I32),
                        Span),
                    Span)
            ]);

    private static ExpressionStatement Literal(SandboxValue value)
        => new(new LiteralExpression(value, Span), Span);

    private static LiteralExpression I32(int value)
        => new(SandboxValue.FromInt32(value), Span);

    private static SandboxValue LiteralValue(Statement statement)
        => statement switch
        {
            ExpressionStatement expression => ((LiteralExpression)expression.Value).Value,
            ReturnStatement ret => ((LiteralExpression)ret.Value).Value,
            _ => throw new InvalidOperationException("Expected a literal statement.")
        };
}
