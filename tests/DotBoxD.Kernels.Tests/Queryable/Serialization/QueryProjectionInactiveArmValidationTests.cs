using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryProjectionInactiveArmValidationTests
{
    [Fact]
    public void Identity_projection_initializer_with_inactive_arms_is_rejected_on_write()
    {
        var projection = new QueryProjection
        {
            Kind = QueryProjectionKind.Identity,
            Path = "Secret.Path",
            TypeName = "Payload",
            Fields = [QueryProjectionField.FromMember("leaked", "AttackerId")],
        };

        AssertInactiveArmRejection(() => JsonSerializer.Serialize(projection, EventQueryJson.Options));
    }

    [Fact]
    public void Member_projection_initializer_with_inactive_arms_is_rejected_on_write()
    {
        var projection = new QueryProjection
        {
            Kind = QueryProjectionKind.Member,
            Path = "AttackerId",
            TypeName = "Payload",
            Fields = [QueryProjectionField.FromConstant("constant", QueryValue.FromString("hidden"))],
        };

        AssertInactiveArmRejection(() => JsonSerializer.Serialize(projection, EventQueryJson.Options));
    }

    [Fact]
    public void Document_serialization_rejects_projection_inactive_arms()
    {
        var document = EventQueryDocument.Create(
            "AttackEvent",
            QueryFilter.MatchAll,
            new QueryProjection
            {
                Kind = QueryProjectionKind.Identity,
                Path = "Secret.Path",
                TypeName = "Payload",
                Fields = [QueryProjectionField.FromMember("leaked", "AttackerId")],
            });

        AssertInactiveArmRejection(() => EventQueryJson.Serialize(document));
    }

    [Fact]
    public void Fingerprint_rejects_projection_inactive_arms()
    {
        var document = EventQueryDocument.Create(
            "AttackEvent",
            QueryFilter.MatchAll,
            new QueryProjection
            {
                Kind = QueryProjectionKind.Member,
                Path = "AttackerId",
                TypeName = "Payload",
                Fields = [QueryProjectionField.FromConstant("constant", QueryValue.FromString("hidden"))],
            });

        AssertInactiveArmRejection(() => QueryFingerprint.Compute(document));
    }

    [Theory]
    [InlineData("""{"kind":"identity","path":"Secret.Path"}""")]
    [InlineData("""{"kind":"identity","type":"Payload","fields":[{"name":"leaked","path":"AttackerId"}]}""")]
    [InlineData("""{"kind":"member","path":"AttackerId","type":"Payload"}""")]
    [InlineData("""{"kind":"member","path":"AttackerId","fields":[{"name":"constant","value":"hidden"}]}""")]
    public void Projection_json_with_inactive_arms_is_rejected_on_read(string json)
        => AssertInactiveArmRejection(() =>
            JsonSerializer.Deserialize<QueryProjection>(json, EventQueryJson.Options));

    private static void AssertInactiveArmRejection(Action action)
    {
        var exception = Record.Exception(action);
        Assert.NotNull(exception);
        Assert.True(
            exception is InvalidOperationException or JsonException,
            $"Expected projection inactive-arm validation, got {exception.GetType().Name}: {exception.Message}");
        Assert.Contains("QueryProjection", exception.Message, StringComparison.Ordinal);
        Assert.True(
            exception.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("union", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("arm", StringComparison.OrdinalIgnoreCase),
            $"Expected inactive union-arm validation message, got: {exception.Message}");
    }
}
