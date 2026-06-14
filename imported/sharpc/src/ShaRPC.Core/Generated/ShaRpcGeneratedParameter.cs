namespace ShaRPC.Core.Generated;

/// <summary>
/// Describes a source-generated ShaRPC service method parameter.
/// </summary>
public readonly record struct ShaRpcGeneratedParameter(
    string Name,
    Type Type,
    int Position,
    bool IsCancellationToken,
    bool HasDefaultValue,
    object? DefaultValue);
