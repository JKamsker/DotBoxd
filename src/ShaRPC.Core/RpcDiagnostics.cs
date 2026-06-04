using System.Diagnostics;

namespace ShaRPC.Core;

/// <summary>
/// Central diagnostic hooks for errors ShaRPC observes on best-effort paths.
/// </summary>
public static class RpcDiagnostics
{
    /// <summary>
    /// Raised when ShaRPC observes an error that cannot be thrown to the original caller.
    /// Diagnostic event handlers are isolated from each other and from RPC internals.
    /// </summary>
    public static event EventHandler<RpcDiagnosticErrorEventArgs>? Error;

    internal static void Report(string operation, Exception error)
    {
        SafeTrace($"{operation}: {error.GetType().Name}: {error.Message}");

        var handler = Error;
        if (handler is null)
        {
            return;
        }

        var args = new RpcDiagnosticErrorEventArgs(operation, error);
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<RpcDiagnosticErrorEventArgs>)subscriber).Invoke(typeof(RpcDiagnostics), args);
            }
            catch (Exception subscriberError)
            {
                SafeTrace($"ShaRPC diagnostic handler failed: {subscriberError}");
            }
        }
    }

    private static void SafeTrace(string message)
    {
        try
        {
            Trace.TraceError(message);
        }
        catch
        {
            // Best-effort fallback logging only: a hostile or faulting TraceListener must never break
            // diagnostic subscriber isolation by propagating out of Report.
        }
    }
}
