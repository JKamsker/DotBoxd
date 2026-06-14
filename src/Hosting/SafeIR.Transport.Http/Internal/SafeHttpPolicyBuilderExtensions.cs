namespace SafeIR.Transport.Http.Internal;

public static class SafeHttpPolicyBuilderExtensions
{
    public static SandboxPolicyBuilder GrantHttpGet(
        this SandboxPolicyBuilder builder,
        IEnumerable<string> allowedHosts,
        long maxResponseBytes,
        IEnumerable<string>? allowedSchemes = null,
        TimeSpan? timeout = null,
        bool allowIpLiterals = false,
        bool allowPrivateNetwork = false,
        long? maxRequestBytes = null)
        => SafeIR.Transport.Http.SafeHttpPolicyBuilderExtensions.GrantHttpGet(
            builder,
            allowedHosts,
            maxResponseBytes,
            allowedSchemes,
            timeout,
            allowIpLiterals,
            allowPrivateNetwork,
            maxRequestBytes);
}
