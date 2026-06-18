namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Describes a generated remote filter/projection package plus the local plugin callback that should run
/// after the remote host accepts an event for that package.
/// </summary>
public sealed record RemoteLocalCallbackRegistration(
    Type EventType,
    RemoteLocalCallbackPayload Payload,
    PluginPackage Package,
    Delegate Handler);

/// <summary>
/// Describes the value a remote host must send across the callback transport after the generated
/// server-side pipeline accepts an event.
/// </summary>
public sealed record RemoteLocalCallbackPayload(
    RemoteLocalCallbackPayloadKind Kind,
    Type Type,
    string? Entrypoint);

public enum RemoteLocalCallbackPayloadKind
{
    /// <summary>The callback receives the original event value.</summary>
    Event,

    /// <summary>The callback receives the value returned by <see cref="RemoteLocalCallbackPayload.Entrypoint"/>.</summary>
    Projection,
}
