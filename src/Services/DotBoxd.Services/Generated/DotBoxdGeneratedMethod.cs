namespace DotBoxd.Services.Generated;

/// <summary>
/// Describes a source-generated DotBoxd service method.
/// </summary>
public readonly record struct DotBoxdGeneratedMethod(
    string Name,
    string WireName,
    Type ReturnType,
    Type? ResultType,
    DotBoxdGeneratedReturnKind ReturnKind,
    bool ReturnsNestedService,
    IReadOnlyList<DotBoxdGeneratedParameter> Parameters);
