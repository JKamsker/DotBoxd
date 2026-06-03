namespace ShaRPC.Core.Exceptions;

/// <summary>
/// Base exception for ShaRPC errors.
/// </summary>
public class ShaRpcException : Exception
{
    public ShaRpcException()
    {
    }

    public ShaRpcException(string message) : base(message)
    {
    }

    public ShaRpcException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a remote RPC call fails.
/// </summary>
public class ShaRpcRemoteException : ShaRpcException
{
    /// <summary>
    /// The type name of the remote exception.
    /// </summary>
    public string RemoteExceptionType { get; }

    public ShaRpcRemoteException(string message, string remoteExceptionType)
        : base(message)
    {
        RemoteExceptionType = remoteExceptionType;
    }
}

/// <summary>
/// Exception thrown when a connection fails.
/// </summary>
public class ShaRpcConnectionException : ShaRpcException
{
    public ShaRpcConnectionException(string message) : base(message)
    {
    }

    public ShaRpcConnectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a request times out.
/// </summary>
public class ShaRpcTimeoutException : ShaRpcException
{
    public ShaRpcTimeoutException(string message) : base(message)
    {
    }
}

/// <summary>
/// Exception thrown when a service, method, or sub-service instance is not found.
/// </summary>
public class ShaRpcNotFoundException : ShaRpcException
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

    public ShaRpcNotFoundException(string message) : this(message, NotFoundKind.Service)
    {
    }

    public ShaRpcNotFoundException(string message, NotFoundKind kind) : base(message)
    {
        Kind = kind;
    }

    /// <summary>Which lookup produced this not-found result.</summary>
    public NotFoundKind Kind { get; }
}

/// <summary>
/// Exception thrown when an inbound ShaRPC frame is malformed or cannot be decoded.
/// </summary>
public class ShaRpcProtocolException : ShaRpcException
{
    public ShaRpcProtocolException(string message) : base(message)
    {
    }
}
