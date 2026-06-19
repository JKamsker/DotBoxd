namespace DotBoxD.Kernels.Sandbox.Values;

internal static class SandboxValueTypeMatcher
{
    /// <summary>
    /// Matches the current validation frame without materializing <see cref="SandboxValue.Type"/>.
    /// Record fields are validated by the caller's recursive stack, so records only need an arity check here.
    /// </summary>
    public static bool MatchesValidationFrame(SandboxValue value, SandboxType expectedType)
    {
        if (expectedType.Arguments.Count == 0)
        {
            return ScalarMatches(value, expectedType.Name);
        }

        if (expectedType.Name == "List" && expectedType.Arguments.Count == 1)
        {
            return value is ListValue list && list.ItemType.Equals(expectedType.Arguments[0]);
        }

        if (expectedType.Name == "Map" && expectedType.Arguments.Count == 2)
        {
            return value is MapValue map &&
                   map.KeyType.Equals(expectedType.Arguments[0]) &&
                   map.ValueType.Equals(expectedType.Arguments[1]);
        }

        return expectedType.IsRecord &&
               value is RecordValue record &&
               record.Fields.Count == expectedType.Arguments.Count;
    }

    public static bool MatchesExactType(SandboxValue value, SandboxType expectedType)
    {
        if (expectedType.Name == SandboxType.RecordName)
        {
            return value is RecordValue record && RecordMatchesExactType(record, expectedType);
        }

        return MatchesValidationFrame(value, expectedType);
    }

    private static bool RecordMatchesExactType(RecordValue record, SandboxType expectedType)
    {
        if (record.Fields.Count != expectedType.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < record.Fields.Count; i++)
        {
            if (!MatchesExactType(record.Fields[i], expectedType.Arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ScalarMatches(SandboxValue value, string expectedName)
        => value switch
        {
            UnitValue => expectedName == SandboxType.Unit.Name,
            BoolValue => expectedName == SandboxType.Bool.Name,
            I32Value => expectedName == SandboxType.I32.Name,
            I64Value => expectedName == SandboxType.I64.Name,
            F64Value => expectedName == SandboxType.F64.Name,
            StringValue => expectedName == SandboxType.String.Name,
            GuidValue => expectedName == SandboxType.Guid.Name,
            OpaqueIdValue id => string.Equals(id.TypeName, expectedName, StringComparison.Ordinal),
            SandboxPathValue => expectedName == SandboxType.SandboxPath.Name,
            SandboxUriValue => expectedName == SandboxType.SandboxUri.Name,
            _ => false
        };
}
