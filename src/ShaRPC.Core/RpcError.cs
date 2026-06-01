using ShaRPC.Core.Exceptions;

namespace ShaRPC.Core;

internal readonly record struct RpcError(string Message, string Type);

internal static class RpcErrors
{
    public const int MaxReflectedValueLength = 256;

    public static RpcError FromException(Exception exception) =>
        exception is ShaRpcNotFoundException
            ? new RpcError("Service not found.", RpcErrorTypes.ServiceNotFound)
            : new RpcError("Internal error.", RpcErrorTypes.InternalError);

    public static RpcError ServiceNotFound() =>
        new(
            "Service not found.",
            RpcErrorTypes.ServiceNotFound);

    public static RpcError Protocol(string message) =>
        new(Truncate(message), RpcErrorTypes.ProtocolError);

    public static string Truncate(string value)
    {
        if (value.Length <= MaxReflectedValueLength)
        {
            return value;
        }

        return value.Substring(0, MaxReflectedValueLength - 3) + "...";
    }
}
