namespace DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;

internal sealed record JsonSchemaObjectContract(
    string Name,
    string[] AllowedProperties,
    string[] RequiredProperties)
{
    public IReadOnlyDictionary<string, string> ConstProperties { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string[]> EnumProperties { get; init; } =
        new Dictionary<string, string[]>();
}
