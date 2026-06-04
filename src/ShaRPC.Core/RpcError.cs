using ShaRPC.Core.Exceptions;

namespace ShaRPC.Core;

internal readonly record struct RpcError(string Message, string Type);

internal static class RpcErrors
{
    public const int MaxReflectedValueLength = 256;

    public static RpcError FromException(
        Exception exception,
        Func<Exception, RpcErrorInfo?>? transformer = null)
    {
        if (exception is ShaRpcNotFoundException notFound)
        {
            // Map to a distinct error type so the caller can branch on service vs method vs instance,
            // and preserve the (truncated) message so logs read clearly. The text only echoes names
            // the caller already supplied, so it discloses nothing new. Framework not-found errors keep
            // this typed mapping and are not routed through the transformer.
            return new RpcError(Truncate(notFound.Message), NotFoundErrorType(notFound.Kind));
        }

        if (transformer is not null)
        {
            RpcErrorInfo? transformed;
            try
            {
                transformed = transformer(exception);
            }
            catch (Exception transformerError)
            {
                // A faulting transformer must never replace a handler error with an unhandled one:
                // fall back to the opaque default and surface the transformer fault to diagnostics.
                RpcDiagnostics.Report("Exception transformer failed", transformerError);
                transformed = null;
            }

            if (transformed is { } info)
            {
                return new RpcError(
                    Truncate(info.Message ?? string.Empty),
                    string.IsNullOrEmpty(info.Type) ? RpcErrorTypes.InternalError : Truncate(info.Type));
            }
        }

        // Default: keep internal failure detail off the wire.
        return new RpcError("Internal error.", RpcErrorTypes.InternalError);
    }

    public static RpcError ServiceNotFound() =>
        new(
            "Service not found.",
            RpcErrorTypes.ServiceNotFound);

    public static RpcError QueueFull() =>
        new(
            "Inbound request queue is full; the call was dropped.",
            RpcErrorTypes.QueueFull);

    public static RpcError Protocol(string message) =>
        new(Truncate(message), RpcErrorTypes.ProtocolError);

    private static string NotFoundErrorType(ShaRpcNotFoundException.NotFoundKind kind) =>
        kind switch
        {
            ShaRpcNotFoundException.NotFoundKind.Method => RpcErrorTypes.MethodNotFound,
            ShaRpcNotFoundException.NotFoundKind.Instance => RpcErrorTypes.InstanceNotFound,
            _ => RpcErrorTypes.ServiceNotFound,
        };

    public static string Truncate(string value)
    {
        if (value.Length <= MaxReflectedValueLength)
        {
            return value;
        }

        var cutAt = MaxReflectedValueLength - 3;

        // Never split a UTF-16 surrogate pair: if the last kept char is a high surrogate, drop it so the
        // result cannot end with an orphaned (malformed) surrogate that breaks strict UTF-8 encoding.
        if (char.IsHighSurrogate(value[cutAt - 1]))
        {
            cutAt--;
        }

        return value.Substring(0, cutAt) + "...";
    }
}
