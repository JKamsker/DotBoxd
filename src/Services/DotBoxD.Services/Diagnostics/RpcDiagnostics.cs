using System.Diagnostics;

namespace DotBoxD.Services.Diagnostics;

/// <summary>
/// Central diagnostic hooks for errors DotBoxD observes on best-effort paths.
/// </summary>
public static class RpcDiagnostics
{
    /// <summary>
    /// Raised when DotBoxD observes an error that cannot be thrown to the original caller.
    /// Diagnostic event handlers are isolated from each other and from RPC internals.
    /// </summary>
    public static event EventHandler<RpcDiagnosticErrorEventArgs>? Error;

    internal static void Report(string operation, Exception error)
    {
        try
        {
            Trace.TraceError($"{operation}: {error.GetType().Name}: {error.Message}");
        }
        catch
        {
        }

        var handler = Error;
        if (handler is null)
        {
            return;
        }

        var args = new RpcDiagnosticErrorEventArgs(operation, error);
        try
        {
            handler.Invoke(null, args);
        }
        catch (Exception firstEx)
        {
            var subscribers = handler.GetInvocationList();
            if (subscribers.Length == 1)
            {
                SafeTrace("DotBoxD diagnostic handler failed", firstEx);
                return;
            }

            foreach (var subscriber in subscribers)
            {
                try
                {
                    // Match the primary dispatch above (sender: null) so a subscriber sees the same
                    // sender whether or not an earlier subscriber threw and forced this retry path.
                    ((EventHandler<RpcDiagnosticErrorEventArgs>)subscriber).Invoke(null, args);
                }
                catch (Exception subscriberError)
                {
                    SafeTrace("DotBoxD diagnostic handler failed", subscriberError);
                }
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
        }
    }

    private static void SafeTrace(string prefix, Exception ex)
    {
        try
        {
            Trace.TraceError($"{prefix}: {ex}");
        }
        catch
        {
        }
    }
}
