namespace DotBoxd.Services.Exceptions;

/// <summary>
/// Base exception for DotBoxd errors.
/// </summary>
public class DotBoxdRpcException : Exception
{
    public DotBoxdRpcException()
    {
    }

    public DotBoxdRpcException(string message) : base(message)
    {
    }

    public DotBoxdRpcException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a remote RPC call fails.
/// </summary>
public class DotBoxdRpcRemoteException : DotBoxdRpcException
{
    /// <summary>
    /// The type name of the remote exception.
    /// </summary>
    public string RemoteExceptionType { get; }

    public DotBoxdRpcRemoteException(string message, string remoteExceptionType)
        : base(message)
    {
        RemoteExceptionType = remoteExceptionType;
    }
}

/// <summary>
/// Exception thrown when a connection fails.
/// </summary>
public class DotBoxdRpcConnectionException : DotBoxdRpcException
{
    public DotBoxdRpcConnectionException(string message) : base(message)
    {
    }

    public DotBoxdRpcConnectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a request times out.
/// </summary>
public class DotBoxdRpcTimeoutException : DotBoxdRpcException
{
    public DotBoxdRpcTimeoutException(string message) : base(message)
    {
    }
}

/// <summary>
/// Exception thrown when a service, method, or sub-service instance is not found.
/// </summary>
public class DotBoxdRpcNotFoundException : DotBoxdRpcException
{
    /// <summary>Distinguishes which lookup produced the not-found result.</summary>
    public enum NotFoundKind
    {
        /// <summary>No service is registered under the requested name.</summary>
        Service,

        /// <summary>The service exists but exposes no method with the requested name.</summary>
        Method,

        /// <summary>The sub-service instance id is unknown or has expired.</summary>
        Instance,
    }

    public DotBoxdRpcNotFoundException(string message) : this(message, NotFoundKind.Service)
    {
    }

    public DotBoxdRpcNotFoundException(string message, NotFoundKind kind) : base(message)
    {
        Kind = kind;
    }

    /// <summary>Which lookup produced this not-found result.</summary>
    public NotFoundKind Kind { get; }
}

/// <summary>
/// Exception thrown when an inbound DotBoxd frame is malformed or cannot be decoded.
/// </summary>
public class DotBoxdRpcProtocolException : DotBoxdRpcException
{
    public DotBoxdRpcProtocolException(string message) : base(message)
    {
    }
}
