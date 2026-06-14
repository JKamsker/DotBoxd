namespace DotBoxd.Services.Generated;

/// <summary>
/// Describes a source-generated DotBoxd service method parameter.
/// </summary>
public readonly record struct DotBoxdGeneratedParameter(
    string Name,
    Type Type,
    int Position,
    bool IsCancellationToken,
    bool HasDefaultValue,
    object? DefaultValue);
