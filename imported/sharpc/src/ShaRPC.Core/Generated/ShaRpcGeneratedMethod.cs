namespace ShaRPC.Core.Generated;

/// <summary>
/// Describes a source-generated ShaRPC service method.
/// </summary>
public readonly record struct ShaRpcGeneratedMethod(
    string Name,
    string WireName,
    Type ReturnType,
    Type? ResultType,
    ShaRpcGeneratedReturnKind ReturnKind,
    bool ReturnsNestedService,
    IReadOnlyList<ShaRpcGeneratedParameter> Parameters);
