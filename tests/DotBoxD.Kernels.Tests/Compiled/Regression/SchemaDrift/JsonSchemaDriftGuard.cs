using System.Text.Json;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;

internal static class JsonSchemaDriftGuard
{
    public static IReadOnlyList<string> SemanticDriftMessages(
        string schemaJson,
        JsonSchemaObjectContract contract)
    {
        using var document = JsonDocument.Parse(schemaJson);
        var candidates = FindCandidateObjects(document.RootElement, contract.AllowedProperties).ToArray();
        if (candidates.Length == 0)
        {
            return [$"{contract.Name} property set is missing from the schema."];
        }

        var failures = candidates
            .Select(candidate => ContractFailures(candidate, contract))
            .OrderBy(candidateFailures => candidateFailures.Count)
            .ThenBy(candidateFailures => string.Join("\n", candidateFailures), StringComparer.Ordinal)
            .First();
        return failures;
    }

    private static IReadOnlyList<string> ContractFailures(
        JsonElement schemaObject,
        JsonSchemaObjectContract contract)
    {
        var failures = new List<string>();
        if (!schemaObject.TryGetProperty("additionalProperties", out var additionalProperties) ||
            additionalProperties.ValueKind != JsonValueKind.False)
        {
            failures.Add($"{contract.Name} additionalProperties must be false.");
        }

        var required = RequiredProperties(schemaObject);
        if (!SameSet(required, contract.RequiredProperties))
        {
            failures.Add(
                $"{contract.Name} required properties drifted. Schema: [{string.Join(", ", required)}]. " +
                $"Expected: [{string.Join(", ", contract.RequiredProperties)}].");
        }

        var properties = schemaObject.GetProperty("properties");
        foreach (var expectedConst in contract.ConstProperties)
        {
            if (!TryGetPropertyValue(properties, expectedConst.Key, "const", out var actual) ||
                actual != expectedConst.Value)
            {
                failures.Add(
                    $"{contract.Name} property '{expectedConst.Key}' const drifted. " +
                    $"Schema: [{actual ?? "<missing>"}]. Expected: [{expectedConst.Value}].");
            }
        }

        foreach (var expectedEnum in contract.EnumProperties)
        {
            if (!TryGetEnumValues(properties, expectedEnum.Key, out var actual) ||
                !SameSet(actual, expectedEnum.Value))
            {
                failures.Add(
                    $"{contract.Name} property '{expectedEnum.Key}' enum drifted. " +
                    $"Schema: [{string.Join(", ", actual)}]. Expected: [{string.Join(", ", expectedEnum.Value)}].");
            }
        }

        return failures;
    }

    private static IEnumerable<JsonElement> FindCandidateObjects(JsonElement element, string[] expected)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("properties", out var properties) &&
                    properties.ValueKind == JsonValueKind.Object)
                {
                    var names = properties.EnumerateObject().Select(p => p.Name).ToArray();
                    if (SameSet(names, expected))
                    {
                        yield return element;
                    }
                }

                foreach (var child in element.EnumerateObject())
                {
                    foreach (var found in FindCandidateObjects(child.Value, expected))
                    {
                        yield return found;
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var found in FindCandidateObjects(item, expected))
                    {
                        yield return found;
                    }
                }
                break;
        }
    }

    private static IReadOnlyList<string> RequiredProperties(JsonElement schemaObject)
    {
        if (!schemaObject.TryGetProperty("required", out var required))
        {
            return [];
        }

        return required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .ToArray()
            : ["<invalid required schema>"];
    }

    private static bool TryGetPropertyValue(
        JsonElement properties,
        string propertyName,
        string keyword,
        out string? value)
    {
        value = null;
        if (!properties.TryGetProperty(propertyName, out var propertySchema) ||
            !propertySchema.TryGetProperty(keyword, out var keywordValue) ||
            keywordValue.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = keywordValue.GetString();
        return true;
    }

    private static bool TryGetEnumValues(
        JsonElement properties,
        string propertyName,
        out IReadOnlyList<string> values)
    {
        values = [];
        if (!properties.TryGetProperty(propertyName, out var propertySchema) ||
            !propertySchema.TryGetProperty("enum", out var enumSchema) ||
            enumSchema.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        values = enumSchema.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
        return true;
    }

    private static bool SameSet(IEnumerable<string> left, IEnumerable<string> right)
        => new HashSet<string>(left, StringComparer.Ordinal)
            .SetEquals(new HashSet<string>(right, StringComparer.Ordinal));
}
