using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryProjectionInitializerValidationTests
{
    [Fact]
    public void Public_construct_projection_initializer_with_null_field_is_rejected_by_projection_json()
        => AssertNullFieldRejection(() =>
            JsonSerializer.Serialize(ConstructProjectionWithNullField(), EventQueryJson.Options));

    [Fact]
    public void Public_construct_projection_initializer_with_null_field_is_rejected_by_document_json()
        => AssertNullFieldRejection(() => EventQueryJson.Serialize(DocumentWithNullProjectionField()));

    [Fact]
    public void Public_construct_projection_initializer_with_null_field_is_rejected_by_fingerprint()
        => AssertNullFieldRejection(() => QueryFingerprint.Compute(DocumentWithNullProjectionField()));

    private static EventQueryDocument DocumentWithNullProjectionField()
        => EventQueryDocument.Create(
            "AttackEvent",
            QueryFilter.MatchAll,
            ConstructProjectionWithNullField());

    private static QueryProjection ConstructProjectionWithNullField()
        => new()
        {
            Kind = QueryProjectionKind.Construct,
            TypeName = "Notice",
            Fields =
            [
                QueryProjectionField.FromMember("attacker", "AttackerId"),
                null!,
            ],
        };

    private static void AssertNullFieldRejection(Action action)
    {
        var exception = Record.Exception(action);
        Assert.NotNull(exception);
        Assert.True(
            exception is InvalidOperationException or JsonException,
            $"Expected InvalidOperationException or JsonException, got {exception.GetType().Name}: {exception.Message}");
        AssertNullFieldMessage(exception);
    }

    private static void AssertNullFieldMessage(Exception exception)
    {
        Assert.Contains("QueryProjection", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Construct", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
