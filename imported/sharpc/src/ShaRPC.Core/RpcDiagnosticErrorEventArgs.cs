namespace ShaRPC.Core;

/// <summary>
/// Describes an internal ShaRPC diagnostic error that was observed but not allowed to
/// interrupt RPC teardown or event dispatch.
/// </summary>
public sealed class RpcDiagnosticErrorEventArgs : EventArgs
{
    public RpcDiagnosticErrorEventArgs(string operation, Exception error)
    {
        Operation = operation;
        Error = error;
    }

    /// <summary>The operation that observed the error.</summary>
    public string Operation { get; }

    /// <summary>The observed error.</summary>
    public Exception Error { get; }
}
